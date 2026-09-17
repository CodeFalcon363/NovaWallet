using System.Text.Json;
using Microsoft.Extensions.Options;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Exceptions;
using NovaWallet.Core.Interfaces;
using NovaWallet.Core.Models;

namespace NovaWallet.Core.Services;

public class TransferService(
    IWalletRepository walletRepository,
    ILedgerTransactionRepository ledgerTransactionRepository,
    IAuditLogRepository auditLogRepository,
    IOutboxRepository outboxRepository,
    IIdempotencyRepository idempotencyRepository,
    IDailyUsageRepository dailyUsageRepository,
    IUnitOfWork unitOfWork,
    ICallerContext callerContext,
    TimeProvider timeProvider,
    IOptions<DailyOutboundLimitOptions> dailyLimitOptions,
    IOptions<IdempotencyKeyTtlOptions> idempotencyKeyTtlOptions)
{
    private const int MaxIdempotencyPollAttempts = 60;
    private const int IdempotencyPollDelayMs = 250;

    /// <summary>
    /// Idempotency decision table (see AI_USAGE.md / README for the race this closes):
    ///   - No row, or an expired Completed row, or ANY Failed row -> claim (INSERT if no row,
    ///     RowVersion-guarded UPDATE if a row exists) and process. A Failed row is claimed via
    ///     the same guarded UPDATE as an expired row specifically so two concurrent retries of a
    ///     failed key can't both slip through and both process — the row transitions through
    ///     Pending either way, so only one claimant wins.
    ///   - A "live" row (Pending, or Completed/Failed not yet expired) enforces the fingerprint
    ///     check — the brief's "reusing a key with a different payload must be rejected" applies
    ///     for as long as the key is live, not just while it's Completed.
    ///   - Completed and not expired -> replay without reprocessing.
    ///   - Pending -> poll; never reclaimed by TTL (see IdempotencyKeyTtlOptions/README: we can't
    ///     safely tell "abandoned" apart from "still legitimately processing").
    /// </summary>
    public async Task<TransferResponse> TransferAsync(string idempotencyKey, TransferRequest request, CancellationToken cancellationToken)
    {
        var fingerprint = IdempotencyFingerprint.Compute(request);
        var ttl = TimeSpan.FromHours(idempotencyKeyTtlOptions.Value.Hours);

        for (var pollAttempt = 0; pollAttempt < MaxIdempotencyPollAttempts; pollAttempt++)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var existing = await idempotencyRepository.GetAsync(idempotencyKey, cancellationToken);

            if (existing is not null)
            {
                var isExpired = existing.ExpiresAtUtc <= now;
                var isLive = existing.Status == IdempotencyStatus.Pending || !isExpired;

                if (isLive && existing.RequestFingerprint != fingerprint)
                {
                    throw new IdempotencyConflictException(idempotencyKey);
                }

                if (existing.Status == IdempotencyStatus.Completed && !isExpired)
                {
                    return await BuildReplayResultAsync(existing, cancellationToken);
                }

                if (existing.Status == IdempotencyStatus.Pending)
                {
                    // Another request with this exact key is currently being processed — wait for
                    // it to finish rather than reprocessing, then re-check its outcome.
                    await Task.Delay(IdempotencyPollDelayMs, cancellationToken);
                    continue;
                }

                // Status == Failed (any expiry), or Completed and expired: claim it for a fresh
                // attempt, guarded so a concurrent claim of the same row can't both win.
                var claimed = await idempotencyRepository.TryClaimForRetryAsync(
                    idempotencyKey, fingerprint, now.Add(ttl), existing.RowVersion, cancellationToken);
                if (!claimed)
                {
                    continue;
                }
            }
            else
            {
                var reserved = await idempotencyRepository.TryReserveAsync(idempotencyKey, fingerprint, now.Add(ttl), cancellationToken);
                if (!reserved)
                {
                    // Lost a race to reserve the same new key — re-check from the top.
                    continue;
                }
            }

            return await ProcessTransferAsync(idempotencyKey, request, cancellationToken);
        }

        throw new IdempotencyStillProcessingException(idempotencyKey);
    }

    private Task<TransferResponse> ProcessTransferAsync(string idempotencyKey, TransferRequest request, CancellationToken cancellationToken) =>
        ConcurrencyRetry.ExecuteAsync(unitOfWork, async () =>
        {
            try
            {
                var result = await ExecuteTransferMutationAsync(request, cancellationToken);
                await idempotencyRepository.MarkCompletedAsync(idempotencyKey, result.TransferId, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch (NovaWalletDomainException)
            {
                await idempotencyRepository.MarkFailedAsync(idempotencyKey, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                throw;
            }
        });

    private async Task<TransferResponse> ExecuteTransferMutationAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        if (request.SourceWalletId == request.DestinationWalletId)
        {
            throw new InvalidTransferException("Source and destination wallets must be different.");
        }

        var source = await walletRepository.GetTrackedAsync(request.SourceWalletId, cancellationToken)
            ?? throw new WalletNotFoundException(request.SourceWalletId);

        var destination = await walletRepository.GetTrackedAsync(request.DestinationWalletId, cancellationToken)
            ?? throw new WalletNotFoundException(request.DestinationWalletId);

        if (source.CustomerId != callerContext.ActorId)
        {
            throw new ForbiddenException("You may only transfer from your own wallet.");
        }

        if (source.BalanceMinor < request.AmountMinor)
        {
            throw new InsufficientFundsException(source.WalletId);
        }

        var usageDateWat = WatClock.TodayWat(timeProvider);
        var dailyUsage = await dailyUsageRepository.GetOrCreateTrackedAsync(source.WalletId, usageDateWat, cancellationToken);

        if (dailyUsage.OutboundTotalMinor + request.AmountMinor > dailyLimitOptions.Value.LimitMinor)
        {
            throw new DailyLimitExceededException(source.WalletId);
        }

        dailyUsage.OutboundTotalMinor += request.AmountMinor;

        var sourceBalanceBefore = source.BalanceMinor;
        var destinationBalanceBefore = destination.BalanceMinor;

        source.BalanceMinor -= request.AmountMinor;
        destination.BalanceMinor += request.AmountMinor;

        var transferId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        ledgerTransactionRepository.Add(new LedgerTransaction
        {
            TransactionId = transferId,
            WalletId = source.WalletId,
            Type = LedgerTransactionType.TransferOut,
            AmountMinor = request.AmountMinor,
            BalanceAfterMinor = source.BalanceMinor,
            CounterpartyWalletId = destination.WalletId,
            CreatedAtUtc = now,
        });

        ledgerTransactionRepository.Add(new LedgerTransaction
        {
            TransactionId = Guid.NewGuid(),
            WalletId = destination.WalletId,
            Type = LedgerTransactionType.TransferIn,
            AmountMinor = request.AmountMinor,
            BalanceAfterMinor = destination.BalanceMinor,
            CounterpartyWalletId = source.WalletId,
            CreatedAtUtc = now,
        });

        auditLogRepository.Append(new AuditLogEntry
        {
            AuditId = Guid.NewGuid(),
            WalletId = source.WalletId,
            Action = AuditActions.TransferDebited,
            ActorId = callerContext.ActorId,
            IpAddress = callerContext.IpAddress,
            BalanceBeforeMinor = sourceBalanceBefore,
            BalanceAfterMinor = source.BalanceMinor,
            CorrelationId = callerContext.CorrelationId,
            CreatedAtUtc = now,
        });

        auditLogRepository.Append(new AuditLogEntry
        {
            AuditId = Guid.NewGuid(),
            WalletId = destination.WalletId,
            Action = AuditActions.TransferCredited,
            ActorId = callerContext.ActorId,
            IpAddress = callerContext.IpAddress,
            BalanceBeforeMinor = destinationBalanceBefore,
            BalanceAfterMinor = destination.BalanceMinor,
            CorrelationId = callerContext.CorrelationId,
            CreatedAtUtc = now,
        });

        outboxRepository.Add(new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            Type = OutboxEventTypes.TransferCompleted,
            PayloadJson = JsonSerializer.Serialize(new
            {
                TransferId = transferId,
                SourceWalletId = source.WalletId,
                DestinationWalletId = destination.WalletId,
                request.AmountMinor,
            }),
            CorrelationId = callerContext.CorrelationId,
            CreatedAtUtc = now,
        });

        return new TransferResponse(transferId, source.BalanceMinor);
    }

    private async Task<TransferResponse> BuildReplayResultAsync(TransferIdempotencyRecord record, CancellationToken cancellationToken)
    {
        var transaction = await ledgerTransactionRepository.GetAsync(record.ResultTransactionId!.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Completed idempotency record '{record.IdempotencyKey}' references a missing transaction.");

        return new TransferResponse(transaction.TransactionId, transaction.BalanceAfterMinor);
    }
}
