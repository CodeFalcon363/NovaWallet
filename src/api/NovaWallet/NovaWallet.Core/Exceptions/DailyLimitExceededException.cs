namespace NovaWallet.Core.Exceptions;

public class DailyLimitExceededException(Guid walletId)
    : NovaWalletDomainException($"Wallet '{walletId}' has exceeded its daily outbound transfer limit.", statusCode: 422);
