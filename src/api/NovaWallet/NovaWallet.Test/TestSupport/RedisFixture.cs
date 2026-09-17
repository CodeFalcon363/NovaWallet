using Testcontainers.Redis;
using Xunit;

namespace NovaWallet.Test.TestSupport;

/// <summary>
/// Real Redis via Testcontainers, shared across the test collection — needed because the
/// distributed rate limiter requires a reachable Redis to construct at all (RateLimiter policy
/// factories run per-request, and ConnectionMultiplexer.Connect fails fast if Redis is
/// unreachable), so any WebApplicationFactory-based test needs this even if it isn't testing
/// rate limiting specifically.
/// </summary>
public class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder().WithImage("redis:7-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
