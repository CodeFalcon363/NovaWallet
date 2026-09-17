using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Exceptions;
using NovaWallet.Core.Interfaces;
using NovaWallet.Core.Models;

namespace NovaWallet.Core.Services;

public class WalletService(
    IWalletRepository walletRepository,
    IWalletQueries walletQueries,
    IStatementQueries statementQueries,
    IAuditQueries auditQueries,
    ILedgerTransactionRepository ledgerTransactionRepository,
    IAuditLogRepository auditLogRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork,
    ICallerContext callerContext)
{
    public async Task<WalletResponse> CreateWalletAsync(CreateWalletRequest request, CancellationToken cancellationToken)
    {
        if (request.CustomerId != callerContext.ActorId)
        {
            throw new ForbiddenException("You may only create a wallet for yourself.");
        }

        var wallet = new Wallet
        {
            WalletId = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            Currency = "NGN",
            BalanceMinor = 0,
            CreatedAtUtc = DateTime.UtcNow,
        };

        walletRepository.Add(wallet);

        auditLogRepository.Append(new AuditLogEntry
        {
            AuditId = Guid.NewGuid(),
            WalletId = wallet.WalletId,
            Action = AuditActions.WalletCreated,
            ActorId = callerContext.ActorId,
            IpAddress = callerContext.IpAddress,
            BalanceBeforeMinor = null,
            BalanceAfterMinor = 0,
            CorrelationId = callerContext.CorrelationId,
            CreatedAtUtc = DateTime.UtcNow,
        });

        outboxRepository.Add(new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            Type = OutboxEventTypes.WalletCreated,
            PayloadJson = JsonSerializer.Serialize(new { wallet.WalletId, wallet.CustomerId }),
            CorrelationId = callerContext.CorrelationId,
            CreatedAtUtc = DateTime.UtcNow,
        });

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlExceptionClassifier.IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateWalletException(request.CustomerId);
        }

        return new WalletResponse(wallet.WalletId, wallet.BalanceMinor, wallet.Currency);
    }

    public async Task<WalletResponse> GetBalanceAsync(Guid walletId, CancellationToken cancellationToken)
    {
        var balance = await walletQueries.GetBalanceAsync(walletId, cancellationToken)
            ?? throw new WalletNotFoundException(walletId);

        if (balance.CustomerId != callerContext.ActorId)
        {
            throw new ForbiddenException("You may only view your own wallet.");
        }

        return new WalletResponse(balance.WalletId, balance.BalanceMinor, balance.Currency);
    }

    public Task<WalletResponse> CreditAsync(Guid walletId, CreditWalletRequest request, CancellationToken cancellationToken) =>
        ConcurrencyRetry.ExecuteAsync(unitOfWork, async () =>
        {
            var wallet = await walletRepository.GetTrackedAsync(walletId, cancellationToken)
                ?? throw new WalletNotFoundException(walletId);

            if (wallet.CustomerId != callerContext.ActorId)
            {
                throw new ForbiddenException("You may only credit your own wallet.");
            }

            var balanceBefore = wallet.BalanceMinor;
            wallet.BalanceMinor += request.AmountMinor;

            ledgerTransactionRepository.Add(new LedgerTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = wallet.WalletId,
                Type = LedgerTransactionType.Credit,
                AmountMinor = request.AmountMinor,
                BalanceAfterMinor = wallet.BalanceMinor,
                CreatedAtUtc = DateTime.UtcNow,
            });

            auditLogRepository.Append(new AuditLogEntry
            {
                AuditId = Guid.NewGuid(),
                WalletId = wallet.WalletId,
                Action = AuditActions.BalanceCredited,
                ActorId = callerContext.ActorId,
                IpAddress = callerContext.IpAddress,
                BalanceBeforeMinor = balanceBefore,
                BalanceAfterMinor = wallet.BalanceMinor,
                CorrelationId = callerContext.CorrelationId,
                CreatedAtUtc = DateTime.UtcNow,
            });

            outboxRepository.Add(new OutboxMessage
            {
                OutboxMessageId = Guid.NewGuid(),
                Type = OutboxEventTypes.WalletCredited,
                PayloadJson = JsonSerializer.Serialize(new { wallet.WalletId, request.AmountMinor, NewBalanceMinor = wallet.BalanceMinor }),
                CorrelationId = callerContext.CorrelationId,
                CreatedAtUtc = DateTime.UtcNow,
            });

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new WalletResponse(wallet.WalletId, wallet.BalanceMinor, wallet.Currency);
        });

    public async Task<PagedResult<StatementEntryResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await AuthorizeWalletAccessAsync(walletId, "view the statement for", cancellationToken);
        return await statementQueries.GetStatementAsync(walletId, page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<AuditEntryResponse>> GetAuditTrailAsync(Guid walletId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await AuthorizeWalletAccessAsync(walletId, "view the audit trail for", cancellationToken);
        return await auditQueries.GetAuditTrailAsync(walletId, page, pageSize, cancellationToken);
    }

    private async Task AuthorizeWalletAccessAsync(Guid walletId, string action, CancellationToken cancellationToken)
    {
        var balance = await walletQueries.GetBalanceAsync(walletId, cancellationToken)
            ?? throw new WalletNotFoundException(walletId);

        if (balance.CustomerId != callerContext.ActorId)
        {
            throw new ForbiddenException($"You may only {action} your own wallet.");
        }
    }
}
