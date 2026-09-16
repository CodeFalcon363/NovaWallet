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
            CreatedAtUtc = DateTime.UtcNow,
        });

        outboxRepository.Add(new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            Type = OutboxEventTypes.WalletCreated,
            PayloadJson = JsonSerializer.Serialize(new { wallet.WalletId, wallet.CustomerId }),
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
}
