namespace NovaWallet.Core.Models;

public record StatementEntryResponse(
    Guid TransactionId,
    string Type,
    long AmountMinor,
    long BalanceAfterMinor,
    Guid? CounterpartyWalletId,
    DateTime CreatedAtUtc);
