using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BidMatrix.Contracts.Analyses;
using BidMatrix.Contracts.Identity;
using BidMatrix.Infrastructure.Analyses;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AnalysisIntakeTests(DatabaseFixture database)
{
    private const string InternalServiceToken = "phase-three-internal-service-token";

    [Fact]
    public async Task PdfIntakeIsTenantScopedIdempotentAndCreatesOneReviewTask()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var customerClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        var currentUser = await LoginOwnerAsync(customerClient);
        var organizationId = currentUser.Organizations.Single().OrganizationId;
        await AddCsrfTokenAsync(customerClient);

        const string idempotencyKey = "analysis-intake-test";
        var created = await CreateAnalysisAsync(customerClient, idempotencyKey);
        var duplicateCreate = await CreateAnalysisAsync(customerClient, idempotencyKey);
        Assert.Equal(created.Id, duplicateCreate.Id);

        var firstUpload = await UploadPdfAsync(customerClient, created.Id);
        var duplicateUpload = await UploadPdfAsync(customerClient, created.Id);
        Assert.False(firstUpload.Duplicate);
        Assert.True(duplicateUpload.Duplicate);
        Assert.Equal(firstUpload.File.Id, duplicateUpload.File.Id);
        Assert.Equal("development_bypass", firstUpload.File.ScanStatus);

        var firstSubmit = await SubmitAsync(customerClient, created.Id);
        var duplicateSubmit = await SubmitAsync(customerClient, created.Id);
        Assert.Equal("queued", firstSubmit.Status);
        Assert.Equal(firstSubmit.WorkflowId, duplicateSubmit.WorkflowId);

        using var requirementsResponse = await customerClient.GetAsync($"/v1/analyses/{created.Id}/requirements");
        requirementsResponse.EnsureSuccessStatusCode();
        var requirements = await requirementsResponse.Content.ReadFromJsonAsync<AnalysisRequirementsResponse>();
        Assert.NotNull(requirements);
        Assert.Equal("notImplemented", requirements.CapabilityStatus);
        Assert.Empty(requirements.Requirements);

        var storage = factory.Services.GetRequiredService<InMemoryObjectStorage>();
        Assert.Single(storage.Objects);
        Assert.Contains(
            storage.Objects.Keys,
            key => key.Contains($"organizations/{organizationId}/analyses/{created.Id}/files/", StringComparison.Ordinal));

        using var internalClient = factory.CreateClient();
        internalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceToken);
        var claimedEvent = await ClaimSubmittedEventAsync(internalClient);

        var intakeRequest = new AnalysisIntakeRequest(organizationId, "analysis-intake-test-correlation");
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await internalClient.PostAsJsonAsync(
                $"/internal/v1/analyses/{created.Id}/intake/processing",
                intakeRequest)).StatusCode);

        var firstTask = await CreateReviewTaskAsync(internalClient, created.Id, intakeRequest);
        var duplicateTask = await CreateReviewTaskAsync(internalClient, created.Id, intakeRequest);
        Assert.Equal(firstTask.TaskId, duplicateTask.TaskId);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await internalClient.PostAsJsonAsync(
                $"/internal/v1/analyses/{created.Id}/intake/requires-review",
                intakeRequest)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await internalClient.PostAsJsonAsync(
                $"/internal/v1/analyses/{created.Id}/intake/requires-review",
                intakeRequest)).StatusCode);

        using var finalResponse = await customerClient.GetAsync($"/v1/analyses/{created.Id}");
        var finalAnalysis = await finalResponse.Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(finalAnalysis);
        Assert.Equal("requires_review", finalAnalysis.Status);

        Assert.Equal(1, await CountReviewTasksAsync(Guid.Parse(created.Id)));
        Assert.Equal(1, await CountSubmittedEventsAsync(Guid.Parse(created.Id)));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await internalClient.PostAsync($"/internal/v1/events/{claimedEvent.EventId}/ack", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await internalClient.PostAsync($"/internal/v1/events/{claimedEvent.EventId}/ack", null)).StatusCode);
    }

    [Fact]
    public async Task InvalidPdfIsRejectedAndProductionCannotEnableDevelopmentBypass()
    {
        var options = new AnalysisOptions { ScanMode = "development_bypass" };
        Assert.Throws<InvalidOperationException>(() => options.Validate(isDevelopment: false));

        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await LoginOwnerAsync(client);
        await AddCsrfTokenAsync(client);
        var analysis = await CreateAnalysisAsync(client, $"invalid-pdf-{Guid.NewGuid():N}");

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("not a pdf"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "invalid.pdf");
        using var response = await client.PostAsync($"/v1/analyses/{analysis.Id}/files", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<CurrentUserResponse> LoginOwnerAsync(HttpClient client)
    {
        await AddCsrfTokenAsync(client);
        using var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new LoginRequest("owner@example.invalid", "phase-three-owner-password"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CurrentUserResponse>()
            ?? throw new InvalidOperationException("Login response was empty.");
    }

    private static async Task AddCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>()
            ?? throw new InvalidOperationException("CSRF response was empty.");
        client.DefaultRequestHeaders.Remove(token.HeaderName);
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);
    }

    private static async Task<AnalysisResponse> CreateAnalysisAsync(HttpClient client, string idempotencyKey)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        using var response = await client.PostAsJsonAsync(
            "/v1/analyses",
            new CreateAnalysisRequest("Synthetic integration RFP"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AnalysisResponse>()
            ?? throw new InvalidOperationException("Create analysis response was empty.");
    }

    private static async Task<AnalysisFileUploadResponse> UploadPdfAsync(HttpClient client, string analysisId)
    {
        const string pdf = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<<>>\n%%EOF\n";
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.ASCII.GetBytes(pdf));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "synthetic-rfp.pdf");
        using var response = await client.PostAsync($"/v1/analyses/{analysisId}/files", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AnalysisFileUploadResponse>()
            ?? throw new InvalidOperationException("Upload response was empty.");
    }

    private static async Task<AnalysisResponse> SubmitAsync(HttpClient client, string analysisId)
    {
        using var response = await client.PostAsync($"/v1/analyses/{analysisId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AnalysisResponse>()
            ?? throw new InvalidOperationException("Submit response was empty.");
    }

    private static async Task<ClaimedEventResponse> ClaimSubmittedEventAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/internal/v1/events/claim?eventType=analysis.submitted.v1&limit=1");
        response.EnsureSuccessStatusCode();
        var events = await response.Content.ReadFromJsonAsync<ClaimEventsResponse>()
            ?? throw new InvalidOperationException("Claim response was empty.");
        return Assert.Single(events.Events);
    }

    private static async Task<ManualReviewTaskResponse> CreateReviewTaskAsync(
        HttpClient client,
        string analysisId,
        AnalysisIntakeRequest request)
    {
        using var response = await client.PostAsJsonAsync(
            $"/internal/v1/analyses/{analysisId}/intake/manual-review-task",
            request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ManualReviewTaskResponse>()
            ?? throw new InvalidOperationException("Review task response was empty.");
    }

    private async Task<int> CountReviewTasksAsync(Guid analysisId)
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from tasks where idempotency_key = $1";
        command.Parameters.AddWithValue($"analysis-manual-review:{analysisId}");
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountSubmittedEventsAsync(Guid analysisId)
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select count(*)
            from outbox_events
            where aggregate_id = $1 and event_type = 'analysis.submitted.v1'
            """;
        command.Parameters.AddWithValue(analysisId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
