using System.Net;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class HealthEndpointTests(DatabaseFixture database)
{
    [Fact]
    public async Task ReadinessEndpointReturnsSuccessWhenPostgresIsAvailable()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient();
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/health/ready", cancellationSource.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
