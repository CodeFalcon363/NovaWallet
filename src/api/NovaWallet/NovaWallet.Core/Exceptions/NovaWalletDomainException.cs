namespace NovaWallet.Core.Exceptions;

/// <summary>
/// Base for domain-level failures. Carries the HTTP status code the API layer should map to,
/// keeping that mapping in one place instead of a type-check switch in the API project.
/// </summary>
public abstract class NovaWalletDomainException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
