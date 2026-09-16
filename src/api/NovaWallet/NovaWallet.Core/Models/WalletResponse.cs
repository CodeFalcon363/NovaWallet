namespace NovaWallet.Core.Models;

public record WalletResponse(Guid WalletId, long BalanceMinor, string Currency);
