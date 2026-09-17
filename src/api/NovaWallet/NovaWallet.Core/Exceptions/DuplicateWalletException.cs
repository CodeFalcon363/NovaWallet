namespace NovaWallet.Core.Exceptions;

public class DuplicateWalletException(string customerId)
    : NovaWalletDomainException($"A wallet already exists for customer '{customerId}'.", statusCode: 409);
