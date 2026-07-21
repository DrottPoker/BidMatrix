using System.Net;
using System.Net.Http.Json;
using BidMatrix.Contracts.Identity;
using BidMatrix.Contracts.Owner;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class OwnerConsoleTests(DatabaseFixture database)
{
    [Fact]
    public async Task OwnerConsoleSupportsTasksGoalsControlsAndAudit()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        await LoginAsync(client);

        var dashboard = await client.GetFromJsonAsync<OwnerDashboardResponse>("/owner/v1/dashboard");
        Assert.NotNull(dashboard);
        Assert.True(dashboard.DraftOnly);
        Assert.True(dashboard.ExternalActionsDisabled);
        Assert.True(dashboard.AuditChainValid);

        var agents = await client.GetFromJsonAsync<OwnerAgentListResponse>("/owner/v1/agents");
        Assert.NotNull(agents);
        Assert.Equal(4, agents.Agents.Count);
        Assert.All(agents.Agents, agent => Assert.NotEmpty(agent.ToolPermissions));

        await AddCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"owner-task-{Guid.NewGuid():N}");
        var createTask = new CreateOwnerTaskRequest(
            "support",
            "Review support response",
            "Review a draft response using only approved product facts.",
            "normal",
            null,
            null,
            JsonObject(),
            JsonObject());
        using var createdResponse = await client.PostAsJsonAsync("/owner/v1/tasks", createTask);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var createdTask = await createdResponse.Content.ReadFromJsonAsync<OwnerTaskResponse>();
        Assert.NotNull(createdTask);

        using var duplicateResponse = await client.PostAsJsonAsync("/owner/v1/tasks", createTask);
        Assert.Equal(HttpStatusCode.Created, duplicateResponse.StatusCode);
        var duplicateTask = await duplicateResponse.Content.ReadFromJsonAsync<OwnerTaskResponse>();
        Assert.Equal(createdTask.Id, duplicateTask?.Id);

        using var cancelledResponse = await client.PostAsJsonAsync(
            $"/owner/v1/tasks/{createdTask.Id}/cancel",
            new CancelOwnerTaskRequest(createdTask.Version, "No longer required."));
        Assert.Equal(HttpStatusCode.OK, cancelledResponse.StatusCode);
        var cancelledTask = await cancelledResponse.Content.ReadFromJsonAsync<OwnerTaskResponse>();
        Assert.Equal("cancelled", cancelledTask?.Status);

        var createGoal = new CreateOwnerGoalRequest(
            "Improve manual review readiness",
            "Make every queued analysis easy to inspect without claiming extraction exists.",
            "manual_review_queue_depth",
            0,
            DateTimeOffset.UtcNow.AddDays(30),
            "active",
            JsonObject());
        using var goalResponse = await client.PostAsJsonAsync("/owner/v1/goals", createGoal);
        Assert.Equal(HttpStatusCode.Created, goalResponse.StatusCode);
        var goal = await goalResponse.Content.ReadFromJsonAsync<OwnerGoalResponse>();
        Assert.NotNull(goal);

        using var goalUpdateResponse = await client.PatchAsJsonAsync(
            $"/owner/v1/goals/{goal.Id}",
            new UpdateOwnerGoalRequest(
                goal.Title,
                goal.Description,
                goal.MetricKey,
                goal.TargetValue,
                goal.TargetDate,
                "paused",
                goal.Constraints,
                goal.Version));
        Assert.Equal(HttpStatusCode.OK, goalUpdateResponse.StatusCode);

        var controls = await client.GetFromJsonAsync<OwnerSystemControlListResponse>("/owner/v1/system-controls");
        Assert.NotNull(controls);
        var external = Assert.Single(controls.Controls, control => control.ControlKey == "externalToolExecutionEnabled");
        using var unsafeControlResponse = await client.PatchAsJsonAsync(
            $"/owner/v1/system-controls/{external.ControlKey}",
            new UpdateSystemControlRequest(true, external.Version, "CONFIRM F0 CONTROL CHANGE"));
        Assert.Equal(HttpStatusCode.Conflict, unsafeControlResponse.StatusCode);

        var allAgents = Assert.Single(controls.Controls, control => control.ControlKey == "allAgentsEnabled");
        using var disableResponse = await client.PatchAsJsonAsync(
            $"/owner/v1/system-controls/{allAgents.ControlKey}",
            new UpdateSystemControlRequest(false, allAgents.Version, "CONFIRM F0 CONTROL CHANGE"));
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<OwnerSystemControlResponse>();
        Assert.NotNull(disabled);
        Assert.False(disabled.Enabled);

        using var restoreResponse = await client.PatchAsJsonAsync(
            $"/owner/v1/system-controls/{allAgents.ControlKey}",
            new UpdateSystemControlRequest(true, disabled.Version, "CONFIRM F0 CONTROL CHANGE"));
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);

        var audit = await client.GetFromJsonAsync<OwnerAuditListResponse>("/owner/v1/audit");
        Assert.NotNull(audit);
        Assert.True(audit.ChainValid);
        Assert.Contains(audit.Events, item => item.Action == "task.created");
        Assert.Contains(audit.Events, item => item.Action == "system_control.updated");
    }

    private static System.Text.Json.JsonElement JsonObject() =>
        System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone();

    private static async Task LoginAsync(HttpClient client)
    {
        await AddCsrfTokenAsync(client);
        using var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new LoginRequest("owner@example.invalid", "phase-three-owner-password"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AddCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Remove(token.HeaderName);
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);
    }
}
