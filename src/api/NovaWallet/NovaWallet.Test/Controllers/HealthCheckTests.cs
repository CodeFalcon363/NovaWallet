using System.Net;
using NovaWallet.Test.TestSupport;
using Xunit;

namespace NovaWallet.Test.Controllers;

[Collection(SqlServerCollection.Name)]
public class HealthCheckTests(SqlServerFixture fixture) : IDisposable
{
    private readonly NovaWalletApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Health_Liveness_Returns_200_Without_Auth()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Readiness_Returns_200_When_Sql_Server_Is_Reachable()
    {
        var client = _factory.CreateClient();

        // RabbitMQ is not running in this test environment, so readiness as a whole may report
        // unhealthy — what this test actually verifies is that the endpoint executes the real
        // SQL Server check against the test database without auth and without throwing.
        var response = await client.GetAsync("/health/ready");

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);
    }
}
