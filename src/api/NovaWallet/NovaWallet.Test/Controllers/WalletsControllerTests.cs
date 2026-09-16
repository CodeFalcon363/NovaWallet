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
public class WalletsControllerTests : IDisposable
{
    private readonly NovaWalletApiFactory _factory;
    private readonly HttpClient _client;

    public WalletsControllerTests(SqlServerFixture fixture)
    {
        _factory = new NovaWalletApiFactory(fixture.ConnectionString);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> GetTokenAsync(string customerId)
    {
        var response = await _client.PostAsJsonAsync("/auth/tokens", new { customerId });
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>();
        return body!.Data.AccessToken;
    }

    private async Task AuthenticateAsync(string customerId)
    {
        var token = await GetTokenAsync(customerId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task CreateWallet_Without_Token_Returns_401()
    {
        var response = await _client.PostAsJsonAsync("/wallets", new { customerId = "no-auth-customer" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateWallet_With_Valid_Token_Returns_201_With_Envelope()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        await AuthenticateAsync(customerId);

        var response = await _client.PostAsJsonAsync("/wallets", new { customerId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<WalletResponse>>();
        Assert.Equal(201, body!.Status);
        Assert.Equal("Wallet created", body.Message);
        Assert.Equal(0, body.Data.BalanceMinor);
        Assert.Equal("NGN", body.Data.Currency);
    }

    [Fact]
    public async Task CreateWallet_With_Mismatched_CustomerId_Returns_403_ProblemDetails()
    {
        await AuthenticateAsync("token-customer");

        var response = await _client.PostAsJsonAsync("/wallets", new { customerId = "someone-else" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(403, problem!.Status);
    }

    [Fact]
    public async Task CreateWallet_Duplicate_Returns_409_ProblemDetails()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        await AuthenticateAsync(customerId);
        await _client.PostAsJsonAsync("/wallets", new { customerId });

        var response = await _client.PostAsJsonAsync("/wallets", new { customerId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(409, problem!.Status);
    }

    [Fact]
    public async Task CreateWallet_Invalid_CustomerId_Returns_400_ValidationProblemDetails()
    {
        await AuthenticateAsync("whoever");

        var response = await _client.PostAsJsonAsync("/wallets", new { customerId = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBalance_Returns_200_With_Envelope()
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        await AuthenticateAsync(customerId);
        var created = await _client.PostAsJsonAsync("/wallets", new { customerId });
        var createdBody = await created.Content.ReadFromJsonAsync<ApiResponse<WalletResponse>>();

        var response = await _client.GetAsync($"/wallets?walletId={createdBody!.Data.WalletId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<WalletResponse>>();
        Assert.Equal(createdBody.Data.WalletId, body!.Data.WalletId);
        Assert.Equal(0, body.Data.BalanceMinor);
    }

    [Fact]
    public async Task GetBalance_Unknown_Wallet_Returns_404_ProblemDetails()
    {
        await AuthenticateAsync("whoever");

        var response = await _client.GetAsync($"/wallets?walletId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(404, problem!.Status);
    }
}
