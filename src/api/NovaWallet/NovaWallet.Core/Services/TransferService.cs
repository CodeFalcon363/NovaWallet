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
    IOptions<DailyOutboundLimitOptions> dailyLimitOptions)
{
    private const int MaxIdempotencyPollAttempts = 60;
    private const int IdempotencyPollDelayMs = 250;

    public async Task<TransferResponse> TransferAsync(string idempotencyKey, TransferRequest request, CancellationToken cancellationToken)
    {
        var fingerprint = IdempotencyFingerprint.Compute(request);

        for (var pollAttempt = 0; pollAttempt < MaxIdempotencyPollAttempts; pollAttempt++)
        {
            var existing = await idempotencyRepository.GetAsync(idempotencyKey, cancellationToken);

            if (existing is not null && existing.RequestFingerprint != fingerprint)
            {
                throw new IdempotencyConflictException(idempotencyKey);
            }

            if (existing is { Status: IdempotencyStatus.Completed })
            {
                return await BuildReplayResultAsync(existing, cancellationToken);
            }

            if (existing is { Status: IdempotencyStatus.Pending })
            {
                // Another request with this exact key is currently being processed — wait for
                // it to finish rather than reprocessing, then re-check its outcome.
                await Task.Delay(IdempotencyPollDelayMs, cancellationToken);
                continue;
            }

            // existing is null (brand-new key) or Status == Failed (finished, safe to retry now).
            if (existing is null)
            {
                var reserved = await idempotencyRepository.TryReserveAsync(idempotencyKey, fingerprint, cancellationToken);
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
