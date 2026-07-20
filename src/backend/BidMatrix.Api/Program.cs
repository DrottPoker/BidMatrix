using System.Text.Json;
using BidMatrix.Contracts.System;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

if (args is ["--healthcheck"])
{
    return await RunHealthCheckAsync();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet(
        "/api",
        () => Results.Ok(new ServiceInfoResponse("BidMatrix API", "0.1.0", "foundation")))
    .WithName("GetServiceInfo");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready");

await app.RunAsync();
return 0;

static async Task<int> RunHealthCheckAsync()
{
    using var client = new HttpClient
    {
        BaseAddress = new Uri("http://127.0.0.1:8080"),
        Timeout = Timeout.InfiniteTimeSpan,
    };
    using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    try
    {
        using var response = await client.GetAsync("/health/ready", cancellationSource.Token);
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
    catch (OperationCanceledException)
    {
        return 1;
    }
}

public partial class Program;
