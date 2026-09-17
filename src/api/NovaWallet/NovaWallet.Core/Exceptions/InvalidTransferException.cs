namespace NovaWallet.Core.Exceptions;

public class InvalidTransferException(string message) : NovaWalletDomainException(message, statusCode: 422);
