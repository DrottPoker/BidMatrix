using System.Text.Json;
using BidMatrix.Application.Audit;
using BidMatrix.Application.Internal;
using BidMatrix.Application.Owner;
using BidMatrix.Application.Tools;
using BidMatrix.Infrastructure.Tools;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BidMatrix.Infrastructure.Internal;

public sealed class PostgresInternalFoundationService(
    NpgsqlDataSource dataSource,
    IPolicyEngine policyEngine,
    IAuditWriter auditWriter,
    IHostEnvironment environment,
    TimeProvider timeProvider) : IInternalFoundationService
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        "queued", "assigned", "running", "waiting_for_input", "waiting_for_approval",
        "completed", "failed", "cancelled",
    };

    public async Task<OwnerTaskRecord?> GetTaskAsync(
        Guid organizationId,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(connection, transaction, organizationId, false, cancellationToken);
        var task = await LoadTaskAsync(connection, transaction, taskId, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return task;
    }

    public async Task<OwnerTaskRecord> UpdateTaskStatusAsync(
        UpdateInternalTaskStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.OrganizationId == Guid.Empty || command.TaskId == Guid.Empty ||
            !Statuses.Contains(command.Status) || command.ExpectedVersion <= 0 ||
            command.ErrorCode?.Length > 100 || command.ErrorMessage?.Length > 2000)
        {
            throw new ToolGatewayException("invalid_task_status_update", "The task status update is invalid.", 400);
        }
        if (command.Status == "completed")
        {
            throw new ToolGatewayException(
                "specialized_completion_required",
                "Completed tasks must use the analysis or agent completion contract so required artifacts are enforced.",
                409);
        }

        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(connection, transaction, command.OrganizationId, false, cancellationToken);
        var current = await LoadTaskAsync(connection, transaction, command.TaskId, true, cancellationToken)
            ?? throw new ToolGatewayException("task_not_found", "The task was not found.", 404);
        if (current.Version != command.ExpectedVersion)
        {
            throw new ToolGatewayException("task_version_conflict", "The task changed before the status update.", 409);
        }
        if (current.Status is "completed" or "failed" or "cancelled")
        {
            throw new ToolGatewayException("task_final", "A final task cannot change status.", 409);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update tasks set status = $2,
                    started_at = case when $2 = 'running' then coalesce(started_at, $3) else started_at end,
                    completed_at = case when $2 in ('failed', 'cancelled') then $3 else null end,
                    error_code = $4,
                    error_message = $5,
                    updated_at = $3,
                    version = version + 1
                where id = $1
                """;
            update.Parameters.AddWithValue(command.TaskId);
            update.Parameters.AddWithValue(command.Status);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue((object?)command.ErrorCode ?? DBNull.Value);
            update.Parameters.AddWithValue((object?)command.ErrorMessage ?? DBNull.Value);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        var result = await LoadTaskAsync(connection, transaction, command.TaskId, false, cancellationToken)
            ?? throw new InvalidOperationException("Updated internal task could not be loaded.");
        await transaction.CommitAsync(cancellationToken);
        await auditWriter.AppendAsync(new AuditEventWrite(
            "service", "agent-worker", "task.status_updated", "task", command.TaskId.ToString(),
            command.OrganizationId, command.CorrelationId, null,
            $"Internal workflow set task status to {command.Status}.", "{}", now), cancellationToken);
        return result;
    }

    public Task<JsonElement> GetCompanyConstitutionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            version = "f0-v1",
            companyIdentity = "BidMatrix",
            productPromise = "Sourced and reviewable RFP decision support.",
            approvedCapabilities = new[] { "pdf-quarantine", "manual-review", "draft-artifacts" },
            draftOnly = true,
            externalEffectsEnabled = false,
        }));

    public async Task<JsonElement> GetMetricsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(connection, transaction, organizationId, false, cancellationToken);
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, sql) in new[]
        {
            ("analysisCount", "select count(*)::int from analyses"),
            ("manualReviewBacklog", "select count(*)::int from tasks where type = 'analysis_manual_review' and status not in ('completed','failed','cancelled')"),
            ("openTaskCount", "select count(*)::int from tasks where status not in ('completed','failed','cancelled')"),
            ("pendingApprovalCount", "select count(*)::int from approvals where status = 'pending'"),
        })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            values[key] = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        }
        await transaction.CommitAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(new { capturedAt = timeProvider.GetUtcNow(), values });
    }

    public Task<JsonElement> SearchKnowledgeAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 500)
            throw new ToolGatewayException("invalid_knowledge_query", "The knowledge query is invalid.", 400);
        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            status = "noApprovedMatches",
            matches = Array.Empty<object>(),
            note = "No approved knowledge documents are indexed in F0.",
        }));
    }

    public async Task<IReadOnlyList<InternalToolCatalogRecord>> ListToolsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select tool_key, display_name, description, risk_level, side_effect_class, enabled, approval_mode
            from tool_definitions order by tool_key
            """;
        var tools = new List<InternalToolCatalogRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tools.Add(new InternalToolCatalogRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetBoolean(5), reader.GetString(6)));
        }
        return tools;
    }

    public async Task<InternalPolicyEvaluationRecord> EvaluateToolAsync(
        InternalPolicyEvaluationCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(connection, transaction, command.OrganizationId, false, cancellationToken);

        ToolDefinitionSnapshot tool;
        await using (var definition = connection.CreateCommand())
        {
            definition.Transaction = transaction;
            definition.CommandText = """
                select id, tool_key, risk_level, side_effect_class, enabled, approval_mode
                from tool_definitions where tool_key = $1
                """;
            definition.Parameters.AddWithValue(command.ToolKey);
            await using var reader = await definition.ExecuteReaderAsync(cancellationToken);
            tool = await reader.ReadAsync(cancellationToken)
                ? new ToolDefinitionSnapshot(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetBoolean(4), reader.GetString(5))
                : throw new ToolGatewayException("unknown_tool", "The tool is not registered.", 404);
        }

        string taskType;
        bool ownerCreated;
        string permissions;
        await using (var context = connection.CreateCommand())
        {
            context.Transaction = transaction;
            context.CommandText = """
                select task.type, task.created_by_type = 'user', version.tool_permissions::text
                from tasks task
                join agent_runs run on run.task_id = task.id
                join agent_versions version on version.id = run.agent_version_id
                join agent_definitions definition on definition.id = version.agent_definition_id
                where task.id = $1 and run.id = $2 and definition.agent_key = $3
                """;
            context.Parameters.AddWithValue(command.TaskId);
            context.Parameters.AddWithValue(command.AgentRunId);
            context.Parameters.AddWithValue(command.AgentKey);
            await using var reader = await context.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ToolGatewayException("invalid_agent_context", "The agent context is invalid.", 403);
            taskType = reader.GetString(0);
            ownerCreated = reader.GetBoolean(1);
            permissions = reader.GetString(2);
        }
        using (var permissionDocument = JsonDocument.Parse(permissions))
        {
            if (!permissionDocument.RootElement.EnumerateArray().Any(item => item.GetString() == command.ToolKey))
                throw new ToolGatewayException("tool_permission_denied", "The active agent version does not permit this tool.", 403);
        }

        var controls = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using (var controlCommand = connection.CreateCommand())
        {
            controlCommand.Transaction = transaction;
            controlCommand.CommandText = "select control_key, enabled from system_controls";
            await using var reader = await controlCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) controls[reader.GetString(0)] = reader.GetBoolean(1);
        }
        string policyVersion;
        await using (var policyCommand = connection.CreateCommand())
        {
            policyCommand.Transaction = transaction;
            policyCommand.CommandText = """
                select policy.policy_key || '-v' || version.version_number
                from policies policy join policy_versions version on version.id = policy.active_version_id
                where policy.policy_key = 'f0-draft-only'
                """;
            policyVersion = (string)(await policyCommand.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Active F0 policy is missing."));
        }
        var result = policyEngine.Evaluate(new PolicyEvaluationContext(
            tool, command.AgentKey, taskType, ownerCreated, controls, environment.EnvironmentName));
        await transaction.CommitAsync(cancellationToken);
        return new InternalPolicyEvaluationRecord(
            result.Decision, result.ReasonCode, result.TechnicallyEnabled, policyVersion);
    }

    private static async Task<OwnerTaskRecord?> LoadTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = TaskSelect + "\nwhere task.id = $1" + (forUpdate ? " for update" : string.Empty);
        command.Parameters.AddWithValue(taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        using var input = JsonDocument.Parse(reader.GetString(9));
        using var constraints = JsonDocument.Parse(reader.GetString(10));
        return new OwnerTaskRecord(
            reader.GetGuid(0), NullableGuid(reader, 1), NullableGuid(reader, 2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            NullableString(reader, 8), input.RootElement.Clone(), constraints.RootElement.Clone(),
            NullableGuid(reader, 11), NullableString(reader, 12), NullableString(reader, 13),
            reader.GetFieldValue<DateTimeOffset>(14), NullableDate(reader, 15), NullableDate(reader, 16),
            reader.GetFieldValue<DateTimeOffset>(17), reader.GetInt32(18));
    }

    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? NullableDate(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private const string TaskSelect = """
        select task.id, task.organization_id, task.goal_id, task.type, task.title, task.description,
            task.priority, task.status, task.assigned_agent_key, task.input::text, task.constraints::text,
            task.result_artifact_id, task.error_code, task.error_message, task.created_at, task.started_at,
            task.completed_at, task.updated_at, task.version
        from tasks task
        """;
}
