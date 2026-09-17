using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Models;
using NovaWallet.Api.Security;
using NovaWallet.Core.Models;
using NovaWallet.Test.TestSupport;
using Xunit;

namespace NovaWallet.Test.Controllers;

[Collection(SqlServerCollection.Name)]
public class TransfersControllerTests : IDisposable
{
    private readonly SqlServerFixture _sqlFixture;
    private readonly RedisFixture _redisFixture;
    private readonly NovaWalletApiFactory _factory;
    private readonly HttpClient _client;

    public TransfersControllerTests(SqlServerFixture sqlFixture, RedisFixture redisFixture)
    {
        _sqlFixture = sqlFixture;
        _redisFixture = redisFixture;
        _factory = new NovaWalletApiFactory(sqlFixture.ConnectionString, redisFixture.ConnectionString);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task AuthenticateAsync(string customerId)
    {
        var tokenResponse = await _client.PostAsJsonAsync("/auth/tokens", new { customerId });
        var body = await tokenResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Data.AccessToken);
    }

    private async Task<Guid> CreateWalletAsync(string customerId)
    {
        await AuthenticateAsync(customerId);
        var response = await _client.PostAsJsonAsync("/wallets", new { customerId });
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<WalletResponse>>();
        return body!.Data.WalletId;
    }

    private async Task CreditAsync(Guid walletId, long amountMinor)
    {
        await _client.PostAsJsonAsync($"/wallets/credit?walletId={walletId}", new { amountMinor });
    }

    [Fact]
    public async Task Transfer_Without_Idempotency_Key_Returns_400()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        var sourceId = await CreateWalletAsync(customerId);
        var destinationId = await CreateWalletAsync($"cust-{Guid.NewGuid():N}");

        var response = await _client.PostAsJsonAsync("/transfers", new { sourceWalletId = sourceId, destinationWalletId = destinationId, amountMinor = 100 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_Succeeds_With_Envelope()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        var sourceId = await CreateWalletAsync(customerId);
        await CreditAsync(sourceId, 10_000);
        // CreateWalletAsync re-authenticates as whichever customerId it's given, so switch back
        // to the source owner before initiating the transfer.
        var destinationId = await CreateWalletAsync($"cust-{Guid.NewGuid():N}");
        await AuthenticateAsync(customerId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { sourceWalletId = sourceId, destinationWalletId = destinationId, amountMinor = 2_500 }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TransferResponse>>();
        Assert.Equal("Transfer completed", body!.Message);
        Assert.Equal(7_500, body.Data.NewSourceBalanceMinor);
    }

    [Fact]
    public async Task Transfer_Insufficient_Funds_Returns_422_ProblemDetails()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        var sourceId = await CreateWalletAsync(customerId);
        var destinationId = await CreateWalletAsync($"cust-{Guid.NewGuid():N}");
        await AuthenticateAsync(customerId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { sourceWalletId = sourceId, destinationWalletId = destinationId, amountMinor = 500 }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(422, problem!.Status);
    }

    [Fact]
    public async Task Transfer_Replay_Same_Key_Returns_Same_Result()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        var sourceId = await CreateWalletAsync(customerId);
        await CreditAsync(sourceId, 5_000);
        var destinationId = await CreateWalletAsync($"cust-{Guid.NewGuid():N}");
        await AuthenticateAsync(customerId);

        var idempotencyKey = Guid.NewGuid().ToString();
        var payload = new { sourceWalletId = sourceId, destinationWalletId = destinationId, amountMinor = 1_000 };

        Task<HttpResponseMessage> Send()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/transfers") { Content = JsonContent.Create(payload) };
            req.Headers.Add("Idempotency-Key", idempotencyKey);
            return _client.SendAsync(req);
        }

        var first = await Send();
        var second = await Send();

        var firstBody = await first.Content.ReadFromJsonAsync<ApiResponse<TransferResponse>>();
        var secondBody = await second.Content.ReadFromJsonAsync<ApiResponse<TransferResponse>>();

        Assert.Equal(firstBody!.Data.TransferId, secondBody!.Data.TransferId);
        Assert.Equal(firstBody.Data.NewSourceBalanceMinor, secondBody.Data.NewSourceBalanceMinor);
    }

    [Fact]
    public async Task Transfer_Response_Includes_Correlation_Id_Header()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        var sourceId = await CreateWalletAsync(customerId);
        await CreditAsync(sourceId, 1_000);
        var destinationId = await CreateWalletAsync($"cust-{Guid.NewGuid():N}");
        await AuthenticateAsync(customerId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { sourceWalletId = sourceId, destinationWalletId = destinationId, amountMinor = 1 }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Transfer_Error_Response_Still_Includes_Correlation_Id_Header()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        var sourceId = await CreateWalletAsync(customerId); // never credited — insufficient funds
        var destinationId = await CreateWalletAsync($"cust-{Guid.NewGuid():N}");
        await AuthenticateAsync(customerId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { sourceWalletId = sourceId, destinationWalletId = destinationId, amountMinor = 1 }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Transfer_Exceeding_Rate_Limit_Returns_429_ProblemDetails()
    {
        // The rate limiter is deliberately global (one Redis-backed counter across all
        // instances/callers, see AddNovaWalletRateLimiting) — sharing the class-level _client
        // would mean this 30-request burst pollutes the counter every other test in this class
        // relies on staying under the limit. Isolate it on its own Redis logical database
        // instead of a separate container, so it still exercises the real distributed limiter.
        var isolatedRedisConnectionString = $"{_redisFixture.ConnectionString},defaultDatabase=1";
        await using var isolatedFactory = new NovaWalletApiFactory(_sqlFixture.ConnectionString, isolatedRedisConnectionString);
        using var isolatedClient = isolatedFactory.CreateClient();

        async Task<string> IssueTokenAsync(string forCustomerId)
        {
            var tokenResponse = await isolatedClient.PostAsJsonAsync("/auth/tokens", new { customerId = forCustomerId });
            var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>();
            return tokenBody!.Data.AccessToken;
        }

        async Task<Guid> CreateWalletAsIsolatedAsync(string forCustomerId)
        {
            isolatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IssueTokenAsync(forCustomerId));
            var walletResponse = await isolatedClient.PostAsJsonAsync("/wallets", new { customerId = forCustomerId });
            var walletBody = await walletResponse.Content.ReadFromJsonAsync<ApiResponse<WalletResponse>>();
            return walletBody!.Data.WalletId;
        }

        var customerId = $"cust-{Guid.NewGuid():N}";
        var sourceId = await CreateWalletAsIsolatedAsync(customerId);
        isolatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IssueTokenAsync(customerId));
        await isolatedClient.PostAsJsonAsync($"/wallets/credit?walletId={sourceId}", new { amountMinor = 1_000_000 });

        var destinationId = await CreateWalletAsIsolatedAsync($"cust-{Guid.NewGuid():N}");
        // Re-authenticate as the source owner for the transfer burst below.
        isolatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IssueTokenAsync(customerId));

        // The sliding-window limiter permits 20 requests / 10s; send more than that in a burst.
        var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/transfers")
            {
                Content = JsonContent.Create(new { sourceWalletId = sourceId, destinationWalletId = destinationId, amountMinor = 1 }),
            };
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return isolatedClient.SendAsync(req);
        }));

        Assert.Contains(responses, r => r.StatusCode == (HttpStatusCode)429);

        var throttled = responses.First(r => r.StatusCode == (HttpStatusCode)429);
        var problem = await throttled.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(429, problem!.Status);
    }
}
