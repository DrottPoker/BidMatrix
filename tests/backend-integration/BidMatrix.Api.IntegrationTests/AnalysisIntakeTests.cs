using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BidMatrix.Contracts.Analyses;
using BidMatrix.Contracts.Identity;
using BidMatrix.Infrastructure.Analyses;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AnalysisIntakeTests(DatabaseFixture database)
{
    private const string InternalServiceToken = "phase-three-internal-service-token";
    private static readonly byte[] EvaluationPdf = BuildEvaluationPdf();

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
        Assert.Equal("notReady", requirements.CapabilityStatus);
        Assert.Equal("not_started", requirements.ExtractionStatus);
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

        var extraction = await ExtractAsync(internalClient, created.Id, intakeRequest);
        var duplicateExtraction = await ExtractAsync(internalClient, created.Id, intakeRequest);
        Assert.Equal("succeeded", extraction.ExtractionStatus);
        Assert.Equal(extraction.Metrics, duplicateExtraction.Metrics);
        Assert.Equal(2, extraction.Metrics.PageCount);
        Assert.Equal(3, extraction.Metrics.RequirementCount);
        Assert.Equal(2, extraction.Metrics.MandatoryRequirementCount);
        Assert.Equal(3, extraction.Metrics.CitedRequirementCount);
        Assert.Equal(1, extraction.Metrics.KeyDateCount);
        Assert.Equal(1, extraction.Metrics.RequestedDocumentCount);
        Assert.Equal(1, extraction.Metrics.EvaluationCriterionCount);
        Assert.Equal("request_for_proposal", Assert.Single(extraction.Documents).DocumentType);
        Assert.All(extraction.Requirements, requirement => Assert.NotEmpty(requirement.Citations));

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

        using var finalRequirementsResponse = await customerClient.GetAsync($"/v1/analyses/{created.Id}/requirements");
        finalRequirementsResponse.EnsureSuccessStatusCode();
        var finalRequirements = await finalRequirementsResponse.Content.ReadFromJsonAsync<AnalysisRequirementsResponse>();
        Assert.NotNull(finalRequirements);
        Assert.Equal("qualityReview", finalRequirements.CapabilityStatus);
        Assert.Empty(finalRequirements.Requirements);
        Assert.Empty(finalRequirements.KeyDates);

        using var ownerReviewResponse = await customerClient.GetAsync($"/owner/v1/analyses/{created.Id}/review");
        ownerReviewResponse.EnsureSuccessStatusCode();
        var ownerReview = await ownerReviewResponse.Content.ReadFromJsonAsync<AnalysisRequirementsResponse>();
        Assert.NotNull(ownerReview);
        Assert.Equal(3, ownerReview.Requirements.Count);
        Assert.Single(ownerReview.KeyDates);
        Assert.Single(ownerReview.RequestedDocuments);
        Assert.Single(ownerReview.EvaluationCriteria);
        Assert.Contains(ownerReview.Requirements, requirement =>
            requirement.Mandatory &&
            requirement.Citations.Any(citation => citation.PageNumber == 2));

        using var publishResponse = await customerClient.PostAsJsonAsync(
            $"/owner/v1/analyses/{created.Id}/publish",
            new PublishAnalysisRequest(
                "All extracted content and exact source citations were checked for customer delivery.",
                "PUBLISH REVIEWED ANALYSIS"));
        publishResponse.EnsureSuccessStatusCode();
        var published = await publishResponse.Content.ReadFromJsonAsync<AnalysisRequirementsResponse>();
        Assert.NotNull(published);
        Assert.True(published.Publication.IsPublished);
        Assert.Equal(0, published.Metrics.PendingReviewCount);

        using var customerResultResponse = await customerClient.GetAsync($"/v1/analyses/{created.Id}/requirements");
        customerResultResponse.EnsureSuccessStatusCode();
        var customerResult = await customerResultResponse.Content.ReadFromJsonAsync<AnalysisRequirementsResponse>();
        Assert.NotNull(customerResult);
        Assert.Equal("ready", customerResult.CapabilityStatus);
        Assert.Equal(3, customerResult.Requirements.Count);
        Assert.Single(customerResult.KeyDates);
        Assert.Single(customerResult.RequestedDocuments);
        Assert.Single(customerResult.EvaluationCriteria);

        Assert.Equal(1, await CountReviewTasksAsync(Guid.Parse(created.Id)));
        Assert.Equal(1, await CountSubmittedEventsAsync(Guid.Parse(created.Id)));
        Assert.Equal(2, await CountAnalysisPagesAsync(Guid.Parse(created.Id)));
        Assert.Equal(3, await CountRequirementsAsync(Guid.Parse(created.Id)));
        Assert.Equal(3, await CountFindingsAsync(Guid.Parse(created.Id)));

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

    [Fact]
    public async Task BlankDigitalPdfRequiresOcrAndDoesNotFabricateRequirements()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var customerClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        var currentUser = await LoginOwnerAsync(customerClient);
        await AddCsrfTokenAsync(customerClient);
        var analysis = await CreateAnalysisAsync(customerClient, $"blank-pdf-{Guid.NewGuid():N}");
        await UploadPdfAsync(customerClient, analysis.Id, BuildBlankPdf());
        await SubmitAsync(customerClient, analysis.Id);

        using var internalClient = factory.CreateClient();
        internalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceToken);
        var intakeRequest = new AnalysisIntakeRequest(
            currentUser.Organizations.Single().OrganizationId,
            "blank-pdf-extraction-test");
        using var processingResponse = await internalClient.PostAsJsonAsync(
            $"/internal/v1/analyses/{analysis.Id}/intake/processing",
            intakeRequest);
        Assert.Equal(HttpStatusCode.NoContent, processingResponse.StatusCode);

        var extraction = await ExtractAsync(internalClient, analysis.Id, intakeRequest);

        Assert.Equal("partial", extraction.ExtractionStatus);
        Assert.Empty(extraction.Requirements);
        Assert.Equal(1, extraction.Metrics.FilesRequiringOcr);
        Assert.Equal(0, extraction.Metrics.FailedFileCount);
        Assert.Equal("requires_ocr", Assert.Single(extraction.Documents).ExtractionStatus);
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

    private static async Task<AnalysisFileUploadResponse> UploadPdfAsync(
        HttpClient client,
        string analysisId,
        byte[]? content = null)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(content ?? EvaluationPdf);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "synthetic-rfp.pdf");
        using var response = await client.PostAsync($"/v1/analyses/{analysisId}/files", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AnalysisFileUploadResponse>()
            ?? throw new InvalidOperationException("Upload response was empty.");
    }

    private static async Task<AnalysisRequirementsResponse> ExtractAsync(
        HttpClient client,
        string analysisId,
        AnalysisIntakeRequest request)
    {
        using var response = await client.PostAsJsonAsync(
            $"/internal/v1/analyses/{analysisId}/intake/extract",
            request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AnalysisRequirementsResponse>()
            ?? throw new InvalidOperationException("Extraction response was empty.");
    }

    private static byte[] BuildEvaluationPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var firstPage = builder.AddPage(PageSize.A4);
        AddLines(firstPage, font, [
            "REQUEST FOR PROPOSAL",
            "REQ-001: The supplier must provide 24/7 managed support.",
            "Proposal submission deadline: September 30 2026.",
            "Background information is not a requirement.",
        ]);
        var secondPage = builder.AddPage(PageSize.A4);
        AddLines(secondPage, font, [
            "SECURITY REQUIREMENTS",
            "SEC-002: Proposals shall include a valid ISO 27001 certificate.",
            "Technical solution evaluation weight: 60%.",
            "Preferred providers should describe optional training.",
        ]);
        return builder.Build();
    }

    private static byte[] BuildBlankPdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        return builder.Build();
    }

    private static void AddLines(
        PdfPageBuilder page,
        PdfDocumentBuilder.AddedFont font,
        IReadOnlyList<string> lines)
    {
        var y = 760;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new PdfPoint(50, y), font);
            y -= 28;
        }
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

    private async Task<int> CountAnalysisPagesAsync(Guid analysisId) =>
        await CountAnalysisRowsAsync("analysis_pages", analysisId);

    private async Task<int> CountRequirementsAsync(Guid analysisId) =>
        await CountAnalysisRowsAsync("analysis_requirements", analysisId);

    private async Task<int> CountFindingsAsync(Guid analysisId) =>
        await CountAnalysisRowsAsync("analysis_findings", analysisId);

    private async Task<int> CountAnalysisRowsAsync(string tableName, Guid analysisId)
    {
        var allowedTable = tableName switch
        {
            "analysis_pages" => "analysis_pages",
            "analysis_requirements" => "analysis_requirements",
            "analysis_findings" => "analysis_findings",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName)),
        };
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select count(*) from {allowedTable} where analysis_id = $1";
        command.Parameters.AddWithValue(analysisId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
