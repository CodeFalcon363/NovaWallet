namespace NovaWallet.Core.Exceptions;

/// <summary>The same Idempotency-Key was reused with a different request payload.</summary>
public class IdempotencyConflictException(string idempotencyKey)
    : NovaWalletDomainException($"Idempotency-Key '{idempotencyKey}' was already used with a different request.", statusCode: 409);
