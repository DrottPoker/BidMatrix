using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BidMatrix.Contracts.Agents;
using BidMatrix.Contracts.Analyses;
using BidMatrix.Contracts.Identity;
using BidMatrix.Contracts.Tools;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AgentRuntimeTests(DatabaseFixture database)
{
    private const string InternalServiceToken = "phase-three-internal-service-token";

    [Fact]
    public async Task AllFourAgentTasksPrepareMaterializeAndRecordUsage()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        var currentUser = await LoginOwnerAsync(owner);
        await AddCsrfTokenAsync(owner);
        var organizationId = currentUser.Organizations.Single().OrganizationId;

        using var internalClient = factory.CreateClient();
        internalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceToken);

        foreach (var agentKey in new[] { "executive", "support", "product-analyst", "engineering" })
        {
            var idempotencyKey = $"agent-runtime-{agentKey}-{Guid.NewGuid():N}";
            var task = await CreateDemoAsync(owner, agentKey, idempotencyKey, null);
            var duplicate = await CreateDemoAsync(owner, agentKey, idempotencyKey, null);
            Assert.Equal(task.TaskId, duplicate.TaskId);

            var claimed = await ClaimAgentEventAsync(internalClient);
            Assert.Equal(task.TaskId, claimed.AggregateId);
            var preparation = await PrepareAsync(internalClient, task, organizationId);
            Assert.Equal(agentKey, preparation.AgentKey);
            Assert.Contains("artifact.createDraft", preparation.AllowedTools);
            Assert.DoesNotContain("email.send", preparation.AllowedTools);

            using var outputDocument = JsonDocument.Parse($$"""
                {
                  "status": "completed",
                  "summary": "Validated integration output for {{agentKey}}",
                  "findings": [],
                  "proposed_actions": [],
                  "artifacts": ["integration_output"],
                  "uncertainties": [],
                  "requires_owner_attention": false
                }
                """);
            var output = outputDocument.RootElement.Clone();
            var artifactId = await CreateOutputArtifactAsync(internalClient, preparation, output);
            var completed = await CompleteAsync(
                internalClient,
                preparation,
                artifactId,
                output);
            Assert.Equal("completed", completed.Status);
            Assert.Equal(17, completed.InputTokens);
            Assert.Equal(23, completed.OutputTokens);

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await internalClient.PostAsync($"/internal/v1/events/{claimed.EventId}/ack", null)).StatusCode);
        }

        using var listResponse = await owner.GetAsync("/owner/v1/agent-runs");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<AgentRunListResponse>();
        Assert.NotNull(list);
        Assert.Contains(list.Runs, run => run.Status == "completed" && run.ModelName.Contains("deterministic", StringComparison.Ordinal));

        var completedRunId = list.Runs.First(run => run.Status == "completed").Id;
        using var runResponse = await owner.GetAsync($"/owner/v1/agent-runs/{completedRunId}");
        runResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task FailedStructuredRunCannotCompleteTask()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var currentUser = await LoginOwnerAsync(owner);
        await AddCsrfTokenAsync(owner);
        var organizationId = currentUser.Organizations.Single().OrganizationId;

        using var invalidInput = JsonDocument.Parse("""{"conversation":"invalid"}""");
        var task = await CreateDemoAsync(
            owner,
            "support",
            $"invalid-agent-{Guid.NewGuid():N}",
            invalidInput.RootElement.Clone());

        using var internalClient = factory.CreateClient();
        internalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceToken);
        var claimed = await ClaimAgentEventAsync(internalClient);
        var preparation = await PrepareAsync(internalClient, task, organizationId);

        using var failureResponse = await internalClient.PostAsJsonAsync(
            $"/internal/v1/agent-tasks/{task.TaskId}/fail",
            new FailAgentRunRequest(
                organizationId,
                preparation.AgentRunId,
                "structured_output_invalid",
                "The deterministic fixture failed schema validation.",
                "agent-runtime-failure-test"));
        failureResponse.EnsureSuccessStatusCode();
        var failed = await failureResponse.Content.ReadFromJsonAsync<AgentRunResponse>();
        Assert.NotNull(failed);
        Assert.Equal("failed", failed.Status);
        Assert.Null(failed.OutputArtifactId);

        Assert.Equal("failed", await GetTaskStatusAsync(Guid.Parse(task.TaskId)));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await internalClient.PostAsync($"/internal/v1/events/{claimed.EventId}/ack", null)).StatusCode);
    }

    private static async Task<AgentDemoTaskResponse> CreateDemoAsync(
        HttpClient client,
        string agentKey,
        string idempotencyKey,
        JsonElement? input)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        using var response = await client.PostAsJsonAsync(
            $"/owner/v1/agent-demos/{agentKey}",
            new CreateAgentDemoRequest(input));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AgentDemoTaskResponse>()
            ?? throw new InvalidOperationException("Agent demo response was empty.");
    }

    private static async Task<ClaimedEventResponse> ClaimAgentEventAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/internal/v1/events/claim?eventType=agent.task.created.v1&limit=1");
        response.EnsureSuccessStatusCode();
        var events = await response.Content.ReadFromJsonAsync<ClaimEventsResponse>()
            ?? throw new InvalidOperationException("Claim response was empty.");
        return Assert.Single(events.Events);
    }

    private static async Task<AgentTaskPreparationResponse> PrepareAsync(
        HttpClient client,
        AgentDemoTaskResponse task,
        string organizationId)
    {
        using var response = await client.PostAsJsonAsync(
            $"/internal/v1/agent-tasks/{task.TaskId}/prepare",
            new PrepareAgentTaskRequest(
                organizationId,
                task.WorkflowId,
                $"agent-runtime-{task.TaskId}",
                "deterministic",
                $"{task.AgentKey}-deterministic-f0"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentTaskPreparationResponse>()
            ?? throw new InvalidOperationException("Preparation response was empty.");
    }

    private static async Task<string> CreateOutputArtifactAsync(
        HttpClient client,
        AgentTaskPreparationResponse preparation,
        JsonElement output)
    {
        var request = new ToolGatewayRequestContract(
            Guid.CreateVersion7().ToString(),
            preparation.TaskId,
            preparation.AgentRunId,
            preparation.AgentKey,
            "artifact.createDraft",
            $"agent-output-{preparation.AgentRunId}",
            JsonSerializer.SerializeToElement(new
            {
                title = $"{preparation.AgentKey} integration output",
                artifactType = $"{preparation.AgentKey}_agent_output",
                content = output,
            }),
            new ToolRequestContext(preparation.OrganizationId, preparation.CorrelationId));
        using var response = await client.PostAsJsonAsync("/internal/v1/tool-gateway/calls", request);
        response.EnsureSuccessStatusCode();
        var toolResult = await response.Content.ReadFromJsonAsync<ToolGatewayResponse>()
            ?? throw new InvalidOperationException("Tool result was empty.");
        Assert.Equal("allowed", toolResult.Decision);
        Assert.NotNull(toolResult.Output);
        return toolResult.Output.Value.GetProperty("artifactId").GetString()
            ?? throw new InvalidOperationException("Artifact ID was missing.");
    }

    private static async Task<AgentRunResponse> CompleteAsync(
        HttpClient client,
        AgentTaskPreparationResponse preparation,
        string artifactId,
        JsonElement output)
    {
        using var response = await client.PostAsJsonAsync(
            $"/internal/v1/agent-tasks/{preparation.TaskId}/complete",
            new CompleteAgentRunRequest(
                preparation.OrganizationId,
                preparation.AgentRunId,
                artifactId,
                output,
                1,
                17,
                23,
                0,
                preparation.CorrelationId));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentRunResponse>()
            ?? throw new InvalidOperationException("Completion response was empty.");
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

    private async Task<string> GetTaskStatusAsync(Guid taskId)
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select status from tasks where id = $1";
        command.Parameters.AddWithValue(taskId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Task was missing."));
    }
}
