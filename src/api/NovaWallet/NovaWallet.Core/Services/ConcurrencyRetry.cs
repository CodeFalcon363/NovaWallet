using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Services;

/// <summary>
/// Shared optimistic-concurrency retry for wallet-mutating operations (Credit, Transfer).
/// Resolved mechanism per BRD NFR-CORE-2: optimistic (RowVersion) concurrency with a bounded
/// retry, not pessimistic row locking. Each attempt must re-read current state itself —
/// this helper only owns the retry/reset loop, not the read-modify-write logic.
/// </summary>
public static class ConcurrencyRetry
{
    public const int DefaultMaxAttempts = 10;

    public static async Task<T> ExecuteAsync<T>(IUnitOfWork unitOfWork, Func<Task<T>> attempt, int maxAttempts = DefaultMaxAttempts)
    {
        for (var i = 1; i < maxAttempts; i++)
        {
            try
            {
                return await attempt();
            }
            catch (DbUpdateConcurrencyException)
            {
                unitOfWork.ResetTracking();

                // Jittered backoff: many competing writers retrying instantly just re-collide.
                // A small random delay, growing with attempt number, spreads retries out so
                // each successive round has a real chance of landing on an uncontended write.
                var backoffMs = Random.Shared.Next(5, 20) * i;
                await Task.Delay(backoffMs);
            }
        }

        // Final attempt: let a concurrency exception propagate to the caller/API layer.
        return await attempt();
    }
}
