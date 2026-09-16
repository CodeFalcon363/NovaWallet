namespace NovaWallet.Core.Exceptions;

/// <summary>A concurrent request with the same Idempotency-Key is still being processed.</summary>
public class IdempotencyStillProcessingException(string idempotencyKey)
    : NovaWalletDomainException($"A request with Idempotency-Key '{idempotencyKey}' is still being processed. Please retry.", statusCode: 409);
