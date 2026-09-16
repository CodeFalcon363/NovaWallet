namespace NovaWallet.Core.Entities;

/// <summary>Shared constants so action/event names can't drift between services (Credit, Transfer, ...).</summary>
public static class AuditActions
{
    public const string WalletCreated = "WalletCreated";
    public const string BalanceCredited = "BalanceCredited";
    public const string TransferDebited = "TransferDebited";
    public const string TransferCredited = "TransferCredited";
}

public static class OutboxEventTypes
{
    public const string WalletCreated = "WalletCreated";
    public const string WalletCredited = "WalletCredited";
    public const string TransferCompleted = "TransferCompleted";
}
