using System.Text.Json;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ContractSurfaceTests(DatabaseFixture database)
{
    [Fact]
    public async Task DevelopmentOpenApiContainsTheRequiredReleaseSurface()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");

        foreach (var (method, path) in RequiredOperations)
        {
            Assert.True(paths.TryGetProperty(path, out var pathItem), $"OpenAPI is missing required release path {path}.");
            Assert.True(
                pathItem.TryGetProperty(method, out _),
                $"OpenAPI is missing required release operation {method.ToUpperInvariant()} {path}.");
        }
    }

    [Fact]
    public async Task ApiResponsesUseDefensiveSecurityHeaders()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api");
        response.EnsureSuccessStatusCode();

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    private static readonly (string Method, string Path)[] RequiredOperations =
    [
        ("post", "/v1/auth/login"),
        ("post", "/v1/auth/logout"),
        ("get", "/v1/me"),
        ("get", "/v1/organizations/current"),
        ("post", "/v1/analyses"),
        ("get", "/v1/analyses"),
        ("get", "/v1/analyses/{analysisId}"),
        ("post", "/v1/analyses/{analysisId}/files"),
        ("post", "/v1/analyses/{analysisId}/submit"),
        ("post", "/v1/analyses/{analysisId}/cancel"),
        ("get", "/v1/analyses/{analysisId}/requirements"),
        ("get", "/owner/v1/dashboard"),
        ("get", "/owner/v1/tasks"),
        ("post", "/owner/v1/tasks"),
        ("get", "/owner/v1/tasks/{taskId}"),
        ("post", "/owner/v1/tasks/{taskId}/cancel"),
        ("get", "/owner/v1/approvals"),
        ("get", "/owner/v1/approvals/{approvalId}"),
        ("post", "/owner/v1/approvals/{approvalId}/approve"),
        ("post", "/owner/v1/approvals/{approvalId}/reject"),
        ("get", "/owner/v1/agents"),
        ("get", "/owner/v1/agents/{agentKey}"),
        ("get", "/owner/v1/agent-runs"),
        ("get", "/owner/v1/agent-runs/{runId}"),
        ("get", "/owner/v1/workflows"),
        ("get", "/owner/v1/workflows/{workflowRunId}"),
        ("get", "/owner/v1/artifacts"),
        ("get", "/owner/v1/artifacts/{artifactId}"),
        ("get", "/owner/v1/audit"),
        ("get", "/owner/v1/goals"),
        ("post", "/owner/v1/goals"),
        ("patch", "/owner/v1/goals/{goalId}"),
        ("get", "/owner/v1/system-controls"),
        ("patch", "/owner/v1/system-controls/{controlKey}"),
        ("post", "/owner/v1/demo/executive-brief"),
        ("post", "/owner/v1/demo/support-draft"),
        ("post", "/owner/v1/demo/product-analysis"),
        ("post", "/owner/v1/demo/engineering-plan"),
        ("get", "/internal/v1/events/claim"),
        ("post", "/internal/v1/events/{eventId}/ack"),
        ("post", "/internal/v1/events/{eventId}/fail"),
        ("post", "/internal/v1/analyses/{analysisId}/intake/extract"),
        ("get", "/internal/v1/tasks/{taskId}"),
        ("post", "/internal/v1/tasks"),
        ("patch", "/internal/v1/tasks/{taskId}/status"),
        ("get", "/internal/v1/context/company-constitution"),
        ("get", "/internal/v1/context/metrics"),
        ("post", "/internal/v1/knowledge/search"),
        ("post", "/internal/v1/agent-runs"),
        ("patch", "/internal/v1/agent-runs/{runId}"),
        ("post", "/internal/v1/artifacts"),
        ("get", "/internal/v1/tools/catalog"),
        ("post", "/internal/v1/tools/evaluate"),
        ("post", "/internal/v1/tools/execute"),
        ("post", "/internal/v1/approvals"),
        ("get", "/internal/v1/approvals/{approvalId}"),
        ("post", "/internal/v1/tool-gateway/calls"),
    ];
}
