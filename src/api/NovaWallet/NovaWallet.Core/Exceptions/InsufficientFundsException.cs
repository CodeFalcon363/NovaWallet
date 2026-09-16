namespace NovaWallet.Core.Exceptions;

public class InsufficientFundsException(Guid walletId)
    : NovaWalletDomainException($"Wallet '{walletId}' has insufficient funds for this transfer.", statusCode: 422);
