using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BidMatrix.Contracts.Internal;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class InternalFoundationEndpointTests(DatabaseFixture database)
{
    private const string InternalServiceToken = "phase-three-internal-service-token";

    [Fact]
    public async Task InternalServiceCanReadConstitutionAndToolCatalog()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var internalClient = factory.CreateClient();
        internalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceToken);

        using var constitutionResponse = await internalClient.GetAsync(
            "/internal/v1/context/company-constitution");
        constitutionResponse.EnsureSuccessStatusCode();
        using var constitution = await JsonDocument.ParseAsync(
            await constitutionResponse.Content.ReadAsStreamAsync());
        Assert.True(constitution.RootElement.GetProperty("draftOnly").GetBoolean());
        Assert.False(constitution.RootElement.GetProperty("externalEffectsEnabled").GetBoolean());

        using var catalogResponse = await internalClient.GetAsync("/internal/v1/tools/catalog");
        catalogResponse.EnsureSuccessStatusCode();
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<InternalToolCatalogResponse>();
        Assert.NotNull(catalog);
        Assert.Contains(catalog.Tools, tool => tool.ToolKey == "artifact.createDraft" && tool.Enabled);
        Assert.Contains(catalog.Tools, tool => tool.ToolKey == "email.send" && !tool.Enabled);
    }

    [Fact]
    public async Task InternalFoundationRoutesRejectUnauthenticatedRequests()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/internal/v1/context/company-constitution")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/internal/v1/tools/catalog")).StatusCode);
    }
}
