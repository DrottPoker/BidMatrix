using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BidMatrix.Application.Tools;
using BidMatrix.Contracts.Identity;
using BidMatrix.Contracts.Tools;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ToolGatewayApprovalTests(DatabaseFixture database)
{
    private const string InternalServiceToken = "phase-three-internal-service-token";
    private static readonly Guid OrganizationId = Guid.Parse("01982000-0000-7000-8000-000000000101");

    [Fact]
    public async Task GatewayNormalizesPayloadEnforcesContextAndReturnsOriginalResult()
    {
        var context = await SeedAgentRunAsync("support", "support");
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient();

        var unauthorized = await client.PostAsJsonAsync(
            "/internal/v1/tool-gateway/calls",
            CreateRequest(context, "artifact.createDraft", "gateway-idempotency", """
                {"title":"Support draft","content":{"z":2,"a":1}}
                """));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceToken);
        var first = await ExecuteAsync(client, CreateRequest(
            context,
            "artifact.createDraft",
            "gateway-idempotency",
            """{"title":"Support draft","content":{"z":2,"a":1}}"""));
        var duplicate = await ExecuteAsync(client, CreateRequest(
            context,
            "artifact.createDraft",
            "gateway-idempotency",
            """{"content":{"a":1.0,"z":2},"title":"Support draft"}"""));

        Assert.Equal("allowed", first.Decision);
        Assert.Equal("completed", first.ExecutionStatus);
        Assert.Equal("alreadyExecuted", duplicate.Decision);
        Assert.Equal(first.ToolCallId, duplicate.ToolCallId);
        Assert.Equal(first.InputHash, duplicate.InputHash);
        Assert.Equal(1, await CountAsync(
            "select count(*) from tool_calls where id = $1",
            Guid.Parse(first.ToolCallId)));
        Assert.Equal(1, await CountAsync(
            "select count(*) from artifacts where created_by_id = $1",
            context.AgentRunId.ToString()));

        var executive = await SeedAgentRunAsync("executive", "executive");
        using var denied = await client.PostAsJsonAsync(
            "/internal/v1/tool-gateway/calls",
            CreateRequest(
                executive,
                "email.send",
                "executive-email-denied",
                """{"to":"customer@example.invalid","subject":"Draft","body":"Not sent"}"""));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task ApprovalsArePayloadBoundRevisionedRaceSafeAndDisabledAfterApproval()
    {
        var context = await SeedAgentRunAsync("engineering", "engineering");
        using var factory = new BidMatrixApiFactory(database);
        using var internalClient = factory.CreateClient();
        internalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceToken);

        const string emailPayload = """
            {"repository":"fixture/repository","title":"Draft pull request","body":"This must not be opened remotely.","sources":["fixture:engineering-task"]}
            """;
        var external = await ExecuteAsync(internalClient, CreateRequest(
            context,
            "github.openPullRequest",
            $"email-approval-{Guid.NewGuid():N}",
            emailPayload));
        Assert.Equal("approvalRequired", external.Decision);
        Assert.Equal("pending", external.ExecutionStatus);
        Assert.NotNull(external.ApprovalId);

        using var ownerClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        await LoginOwnerAsync(ownerClient);
        await AddCsrfTokenAsync(ownerClient);

        var approval = await GetApprovalAsync(ownerClient, external.ApprovalId!);
        Assert.Equal("pending", approval.Status);
        Assert.False(approval.TechnicallyEnabled);
        Assert.Equal(CanonicalJson.Hash(approval.NormalizedPayload), approval.PayloadHash);

        using var changedDocument = JsonDocument.Parse("""
            {"repository":"attacker/repository","title":"Changed","body":"Changed"}
            """);
        var invalidated = await DecideAsync(
            ownerClient,
            approval,
            "approve",
            changedDocument.RootElement.Clone());
        Assert.Equal("invalidated", invalidated.Status);
        Assert.Equal("notStarted", invalidated.ExecutionStatus);

        var second = await ExecuteAsync(internalClient, CreateRequest(
            context,
            "github.openPullRequest",
            $"email-approval-{Guid.NewGuid():N}",
            emailPayload));
        var secondApproval = await GetApprovalAsync(ownerClient, second.ApprovalId!);
        var approved = await DecideAsync(
            ownerClient,
            secondApproval,
            "approve",
            secondApproval.NormalizedPayload);
        Assert.Equal("approved", approved.Status);
        Assert.Equal("disabled", approved.ExecutionStatus);
        Assert.False(approved.TechnicallyEnabled);

        var duplicateDecision = await DecideAsync(
            ownerClient,
            secondApproval,
            "reject",
            secondApproval.NormalizedPayload);
        Assert.Equal("approved", duplicateDecision.Status);
        Assert.Equal(approved.Version, duplicateDecision.Version);

        var third = await ExecuteAsync(internalClient, CreateRequest(
            context,
            "github.openPullRequest",
            $"email-approval-{Guid.NewGuid():N}",
            emailPayload));
        var thirdApproval = await GetApprovalAsync(ownerClient, third.ApprovalId!);
        using var revisionDocument = JsonDocument.Parse("""
            {"repository":"fixture/repository","title":"Owner revised draft","body":"Still not opened."}
            """);
        var revision = await DecideAsync(
            ownerClient,
            thirdApproval,
            "editAndCreateRevision",
            revisionDocument.RootElement.Clone());
        Assert.Equal("pending", revision.Status);
        Assert.Equal(thirdApproval.Id, revision.SupersedesApprovalId);
        Assert.NotEqual(thirdApproval.PayloadHash, revision.PayloadHash);
        Assert.Equal("invalidated", (await GetApprovalAsync(ownerClient, thirdApproval.Id)).Status);

        var fourth = await ExecuteAsync(internalClient, CreateRequest(
            context,
            "github.openPullRequest",
            $"email-approval-{Guid.NewGuid():N}",
            emailPayload));
        var fourthApproval = await GetApprovalAsync(ownerClient, fourth.ApprovalId!);
        var approveTask = DecideAsync(ownerClient, fourthApproval, "approve", fourthApproval.NormalizedPayload);
        var rejectTask = DecideAsync(ownerClient, fourthApproval, "reject", fourthApproval.NormalizedPayload);
        var raceResults = await Task.WhenAll(approveTask, rejectTask);
        Assert.All(raceResults, result => Assert.Contains(result.Status, new[] { "approved", "rejected" }));
        Assert.Single(raceResults.Select(result => result.Status).Distinct(StringComparer.Ordinal));

        Assert.True(await CountAsync(
            "select count(*) from audit_events where action like 'tool_gateway.%' or action like 'approval.%'") >= 8);
        Assert.Equal(0, await CountAsync(
            "select count(*) from tool_calls where decision = 'approvalRequired' and execution_status = 'completed'"));
    }

    private async Task<AgentRunContext> SeedAgentRunAsync(string agentKey, string taskType)
    {
        var taskId = Guid.CreateVersion7();
        var agentRunId = Guid.CreateVersion7();
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var task = connection.CreateCommand())
        {
            task.Transaction = transaction;
            task.CommandText = """
                insert into tasks (
                    id, organization_id, type, title, description, priority, status, input,
                    constraints, created_by_type, created_by_id, created_at, updated_at, version,
                    idempotency_key
                )
                values (
                    $1, $2, $3, 'Tool Gateway test task', '', 'normal', 'running', '{}'::jsonb,
                    '{}'::jsonb, 'test', 'tool-gateway-tests', now(), now(), 1, $4
                )
                """;
            task.Parameters.AddWithValue(taskId);
            task.Parameters.AddWithValue(OrganizationId);
            task.Parameters.AddWithValue(taskType);
            task.Parameters.AddWithValue($"gateway-test-{taskId:N}");
            await task.ExecuteNonQueryAsync();
        }

        await using (var run = connection.CreateCommand())
        {
            run.Transaction = transaction;
            run.CommandText = """
                insert into agent_runs (
                    id, organization_id, task_id, agent_version_id, status, model_name, prompt_version,
                    input_hash, started_at
                )
                select $1, $2, $3, definition.active_version_id, 'running', 'deterministic-test',
                       'f0-development-v1', repeat('0', 64), now()
                from agent_definitions definition
                where definition.agent_key = $4
                """;
            run.Parameters.AddWithValue(agentRunId);
            run.Parameters.AddWithValue(OrganizationId);
            run.Parameters.AddWithValue(taskId);
            run.Parameters.AddWithValue(agentKey);
            Assert.Equal(1, await run.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        return new AgentRunContext(taskId, agentRunId, agentKey);
    }

    private static ToolGatewayRequestContract CreateRequest(
        AgentRunContext context,
        string toolKey,
        string idempotencyKey,
        string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return new ToolGatewayRequestContract(
            Guid.CreateVersion7().ToString(),
            context.TaskId.ToString(),
            context.AgentRunId.ToString(),
            context.AgentKey,
            toolKey,
            idempotencyKey,
            document.RootElement.Clone(),
            new ToolRequestContext(OrganizationId.ToString(), $"gateway-test-{Guid.NewGuid():N}"));
    }

    private static async Task<ToolGatewayResponse> ExecuteAsync(
        HttpClient client,
        ToolGatewayRequestContract request)
    {
        using var response = await client.PostAsJsonAsync("/internal/v1/tool-gateway/calls", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ToolGatewayResponse>()
            ?? throw new InvalidOperationException("Tool Gateway response was empty.");
    }

    private static async Task<ApprovalResponse> GetApprovalAsync(HttpClient client, string approvalId)
    {
        using var response = await client.GetAsync($"/owner/v1/approvals/{approvalId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApprovalResponse>()
            ?? throw new InvalidOperationException("Approval response was empty.");
    }

    private static async Task<ApprovalResponse> DecideAsync(
        HttpClient client,
        ApprovalResponse approval,
        string decision,
        JsonElement payload)
    {
        using var response = await client.PostAsJsonAsync(
            $"/owner/v1/approvals/{approval.Id}/decision",
            new ApprovalDecisionRequest(decision, payload, approval.Version, "Integration test decision"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApprovalResponse>()
            ?? throw new InvalidOperationException("Approval decision response was empty.");
    }

    private static async Task LoginOwnerAsync(HttpClient client)
    {
        await AddCsrfTokenAsync(client);
        using var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new LoginRequest("owner@example.invalid", "phase-three-owner-password"));
        response.EnsureSuccessStatusCode();
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

    private async Task<int> CountAsync(string sql, object? value = null)
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (value is not null)
        {
            command.Parameters.AddWithValue(value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record AgentRunContext(Guid TaskId, Guid AgentRunId, string AgentKey);
}
