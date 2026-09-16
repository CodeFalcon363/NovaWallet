namespace NovaWallet.Core.Exceptions;

public class WalletNotFoundException(Guid walletId)
    : NovaWalletDomainException($"Wallet '{walletId}' was not found.", statusCode: 404);
