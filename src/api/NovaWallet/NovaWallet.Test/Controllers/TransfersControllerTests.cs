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
    private readonly NovaWalletApiFactory _factory;
    private readonly HttpClient _client;

    public TransfersControllerTests(SqlServerFixture fixture)
    {
        _factory = new NovaWalletApiFactory(fixture.ConnectionString);
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
}
