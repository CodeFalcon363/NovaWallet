namespace NovaWallet.Core.Exceptions;

/// <summary>Caller is authenticated but not authorized for the wallet/action in question (NFR-SEC-2).</summary>
public class ForbiddenException(string message) : NovaWalletDomainException(message, statusCode: 403);
