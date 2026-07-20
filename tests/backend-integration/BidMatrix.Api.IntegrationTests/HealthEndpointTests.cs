using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BidMatrix.Api.IntegrationTests;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ReadinessEndpointReturnsSuccess()
    {
        using var client = factory.CreateClient();
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var response = await client.GetAsync("/health/ready", cancellationSource.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
