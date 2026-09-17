using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using NovaWallet.Api;

namespace NovaWallet.Test.TestSupport;

/// <summary>Boots the real API pipeline (auth, exception handling, envelope) against a test database.</summary>
public class NovaWalletApiFactory(string sqlConnectionString, string redisConnectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovaWalletDb"] = sqlConnectionString,
                ["Redis:ConnectionString"] = redisConnectionString,
            });
        });
    }
}
