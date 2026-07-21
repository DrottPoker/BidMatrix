using System.Data;
using System.Text.Json;
using BidMatrix.Application.Audit;
using BidMatrix.Application.Owner;
using BidMatrix.Application.Tools;
using BidMatrix.Infrastructure.Tools;
using Npgsql;

namespace BidMatrix.Infrastructure.Owner;

public sealed class PostgresOwnerConsoleService(
    NpgsqlDataSource dataSource,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IOwnerConsoleService
{
    private static readonly HashSet<string> TaskStatuses = new(StringComparer.Ordinal)
    {
        "queued", "assigned", "running", "waiting_for_input", "waiting_for_approval",
        "completed", "failed", "cancelled",
    };

    private static readonly HashSet<string> GoalStatuses = new(StringComparer.Ordinal)
    {
        "draft", "active", "paused", "achieved", "cancelled",
    };

    private static readonly HashSet<string> AgentKeys = new(StringComparer.Ordinal)
    {
        "executive", "support", "product-analyst", "engineering",
    };

    private static readonly IReadOnlyDictionary<string, bool> LockedF0Controls =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["externalCommunicationEnabled"] = false,
            ["externalSpendingEnabled"] = false,
            ["externalToolExecutionEnabled"] = false,
            ["systemDraftOnlyMode"] = true,
        };

    public async Task<OwnerDashboardRecord> GetDashboardAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        ValidateOrganization(organizationId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, organizationId, cancellationToken);

        var analysesByStatus = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "select status, count(*)::int from analyses group by status order by status";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                analysesByStatus[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var openTasks = await ScalarIntAsync(connection, transaction,
            "select count(*)::int from tasks where status not in ('completed', 'failed', 'cancelled')", cancellationToken);
        var pendingApprovals = await ScalarIntAsync(connection, transaction,
            "select count(*)::int from approvals where status = 'pending'", cancellationToken);
        var workflowFailures = await ScalarIntAsync(connection, transaction,
            "select count(*)::int from workflow_runs where status = 'failed'", cancellationToken);
        var runs = await LoadAgentRunsAsync(connection, transaction, null, 8, cancellationToken);
        var controls = await LoadSystemControlsAsync(connection, transaction, cancellationToken);
        var auditValid = await VerifyAuditLinksAsync(connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new OwnerDashboardRecord(
            analysesByStatus,
            openTasks,
            pendingApprovals,
            workflowFailures,
            runs,
            controls,
            auditValid,
            timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<OwnerTaskRecord>> ListTasksAsync(
        Guid organizationId,
        string? status,
        string? type,
        string? agentKey,
        string? priority,
        CancellationToken cancellationToken = default)
    {
        ValidateOrganization(organizationId);
        if (status is not null && !TaskStatuses.Contains(status))
        {
            throw new OwnerConsoleException("invalid_task_status", "The task status filter is invalid.", 400);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, organizationId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = TaskSelect + """

            where ($1::text is null or task.status = $1)
              and ($2::text is null or task.type = $2)
              and ($3::text is null or task.assigned_agent_key = $3)
              and ($4::text is null or task.priority = $4)
            order by task.created_at desc
            limit 250
            """;
        command.Parameters.AddWithValue((object?)NormalizeFilter(status) ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)NormalizeFilter(type) ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)NormalizeFilter(agentKey) ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)NormalizeFilter(priority) ?? DBNull.Value);
        var tasks = new List<OwnerTaskRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(MapTask(reader));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return tasks;
    }

    public async Task<OwnerTaskRecord?> GetTaskAsync(
        Guid organizationId,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        ValidateOrganization(organizationId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, organizationId, cancellationToken);
        var task = await LoadTaskAsync(connection, transaction, taskId, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return task;
    }

    public async Task<OwnerTaskRecord> CreateTaskAsync(
        CreateOwnerTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateTask(command);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await SetOwnerContextAsync(connection, transaction, command.OrganizationId, cancellationToken);

        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = TaskSelect + "\nwhere task.organization_id = $1 and task.owner_idempotency_key = $2";
            existing.Parameters.AddWithValue(command.OrganizationId);
            existing.Parameters.AddWithValue(command.IdempotencyKey);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var result = MapTask(reader);
                await reader.CloseAsync();
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
        }

        var taskId = Guid.CreateVersion7();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                insert into tasks (
                    id, organization_id, goal_id, type, title, description, priority, status,
                    assigned_agent_key, input, constraints, created_by_type, created_by_id,
                    created_at, updated_at, version, owner_idempotency_key
                ) values (
                    $1, $2, $3, $4, $5, $6, $7, 'queued', $8, $9::jsonb, $10::jsonb,
                    'user', $11, $12, $12, 1, $13
                )
                """;
            insert.Parameters.AddWithValue(taskId);
            insert.Parameters.AddWithValue(command.OrganizationId);
            insert.Parameters.AddWithValue((object?)command.GoalId ?? DBNull.Value);
            insert.Parameters.AddWithValue(command.Type.Trim());
            insert.Parameters.AddWithValue(command.Title.Trim());
            insert.Parameters.AddWithValue(command.Description.Trim());
            insert.Parameters.AddWithValue(command.Priority.Trim());
            insert.Parameters.AddWithValue((object?)command.AssignedAgentKey ?? DBNull.Value);
            insert.Parameters.AddWithValue(CanonicalJson.Normalize(command.Input));
            insert.Parameters.AddWithValue(CanonicalJson.Normalize(command.Constraints));
            insert.Parameters.AddWithValue(command.OwnerUserId.ToString());
            insert.Parameters.AddWithValue(now);
            insert.Parameters.AddWithValue(command.IdempotencyKey);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (command.AssignedAgentKey is not null)
        {
            var workflowId = $"agent-task-{taskId}";
            var payload = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                organizationId = command.OrganizationId,
                correlationId = command.CorrelationId,
                payload = new { taskId, agentKey = command.AssignedAgentKey, workflowId },
            });
            await using var outbox = connection.CreateCommand();
            outbox.Transaction = transaction;
            outbox.CommandText = """
                insert into outbox_events (
                    id, event_type, aggregate_type, aggregate_id, payload,
                    occurred_at, available_at, attempt_count
                ) values ($1, 'agent.task.created.v1', 'task', $2, $3::jsonb, $4, $4, 0)
                """;
            outbox.Parameters.AddWithValue(Guid.CreateVersion7());
            outbox.Parameters.AddWithValue(taskId);
            outbox.Parameters.AddWithValue(payload);
            outbox.Parameters.AddWithValue(now);
            await outbox.ExecuteNonQueryAsync(cancellationToken);
        }

        var created = await LoadTaskAsync(connection, transaction, taskId, false, cancellationToken)
            ?? throw new InvalidOperationException("Created owner task could not be loaded.");
        await transaction.CommitAsync(cancellationToken);
        await WriteAuditAsync(command.OwnerUserId, "task.created", "task", taskId, command.OrganizationId,
            command.CorrelationId, "Owner created a task.", cancellationToken);
        return created;
    }

    public async Task<OwnerTaskRecord> CancelTaskAsync(
        CancelOwnerTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.OrganizationId == Guid.Empty || command.OwnerUserId == Guid.Empty ||
            command.TaskId == Guid.Empty || command.ExpectedVersion <= 0 || command.Note?.Length > 1000)
        {
            throw new OwnerConsoleException("invalid_task_cancellation", "The task cancellation request is invalid.", 400);
        }

        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, command.OrganizationId, cancellationToken);
        var current = await LoadTaskAsync(connection, transaction, command.TaskId, true, cancellationToken)
            ?? throw new OwnerConsoleException("task_not_found", "The task was not found.", 404);
        if (current.Status is "completed" or "failed" or "cancelled")
        {
            await transaction.CommitAsync(cancellationToken);
            return current;
        }
        if (current.Version != command.ExpectedVersion)
        {
            throw new OwnerConsoleException("task_version_conflict", "The task changed before cancellation.", 409);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update tasks set status = 'cancelled', completed_at = $2, updated_at = $2,
                    error_code = 'owner_cancelled', error_message = $3, version = version + 1
                where id = $1
                """;
            update.Parameters.AddWithValue(command.TaskId);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue((object?)command.Note ?? "Cancelled by owner.");
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var cancelled = await LoadTaskAsync(connection, transaction, command.TaskId, false, cancellationToken)
            ?? throw new InvalidOperationException("Cancelled task could not be loaded.");
        await transaction.CommitAsync(cancellationToken);
        await WriteAuditAsync(command.OwnerUserId, "task.cancelled", "task", command.TaskId,
            command.OrganizationId, command.CorrelationId, "Owner cancelled a task.", cancellationToken);
        return cancelled;
    }

    public async Task<IReadOnlyList<OwnerAgentDefinitionRecord>> ListAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, null, cancellationToken);
        var agents = await LoadAgentsAsync(connection, transaction, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return agents;
    }

    public async Task<OwnerAgentDefinitionRecord?> GetAgentAsync(
        string agentKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, null, cancellationToken);
        var agents = await LoadAgentsAsync(connection, transaction, agentKey, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return agents.SingleOrDefault();
    }

    public async Task<IReadOnlyList<OwnerAgentRunRecord>> ListAgentRunsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        ValidateOrganization(organizationId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, organizationId, cancellationToken);
        var runs = await LoadAgentRunsAsync(connection, transaction, null, 250, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return runs;
    }

    public async Task<OwnerAgentRunRecord?> GetAgentRunAsync(
        Guid organizationId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ValidateOrganization(organizationId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, organizationId, cancellationToken);
        var runs = await LoadAgentRunsAsync(connection, transaction, runId, 1, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return runs.SingleOrDefault();
    }

    public async Task<IReadOnlyList<OwnerWorkflowRecord>> ListWorkflowsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await LoadWorkflowsPublicAsync(organizationId, null, cancellationToken);

    public async Task<OwnerWorkflowRecord?> GetWorkflowAsync(
        Guid organizationId,
        Guid workflowRunId,
        CancellationToken cancellationToken = default) =>
        (await LoadWorkflowsPublicAsync(organizationId, workflowRunId, cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<OwnerArtifactRecord>> ListArtifactsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await LoadArtifactsPublicAsync(organizationId, null, cancellationToken);

    public async Task<OwnerArtifactRecord?> GetArtifactAsync(
        Guid organizationId,
        Guid artifactId,
        CancellationToken cancellationToken = default) =>
        (await LoadArtifactsPublicAsync(organizationId, artifactId, cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<OwnerAuditEventRecord>> ListAuditAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, null, cancellationToken);
        var records = new List<OwnerAuditEventRecord>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select * from (
                select id, sequence_number, actor_type, actor_id, action, target_type, target_id,
                    organization_id, request_id, trace_id, summary, previous_hash, event_hash,
                    previous_hash is not distinct from lag(event_hash) over (order by sequence_number) as chain_link_valid,
                    created_at
                from audit_events
            ) audit
            order by sequence_number desc
            limit 500
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new OwnerAuditEventRecord(
                reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), NullableString(reader, 5), NullableString(reader, 6),
                NullableGuid(reader, 7), NullableString(reader, 8), NullableString(reader, 9),
                reader.GetString(10), NullableString(reader, 11), reader.GetString(12),
                reader.GetBoolean(13), reader.GetFieldValue<DateTimeOffset>(14)));
        }
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return records;
    }

    public async Task<IReadOnlyList<OwnerGoalRecord>> ListGoalsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = GoalSelect + " order by updated_at desc limit 250";
        var goals = new List<OwnerGoalRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) goals.Add(MapGoal(reader));
        return goals;
    }

    public async Task<OwnerGoalRecord> CreateGoalAsync(
        CreateOwnerGoalCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateGoal(command.Title, command.Description, command.Status, command.Constraints);
        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                insert into goals (id, title, description, metric_key, target_value, target_date,
                    status, constraints, created_by_user_id, created_at, updated_at, version)
                values ($1, $2, $3, $4, $5, $6, $7, $8::jsonb, $9, $10, $10, 1)
                """;
            AddGoalParameters(insert, id, command.Title, command.Description, command.MetricKey,
                command.TargetValue, command.TargetDate, command.Status, command.Constraints,
                command.OwnerUserId, now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        var result = await LoadGoalAsync(connection, id, cancellationToken)
            ?? throw new InvalidOperationException("Created goal could not be loaded.");
        await WriteAuditAsync(command.OwnerUserId, "goal.created", "goal", id, null,
            command.CorrelationId, "Owner created a goal.", cancellationToken);
        return result;
    }

    public async Task<OwnerGoalRecord> UpdateGoalAsync(
        UpdateOwnerGoalCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateGoal(command.Title, command.Description, command.Status, command.Constraints);
        if (command.ExpectedVersion <= 0)
        {
            throw new OwnerConsoleException("invalid_goal_version", "Expected goal version must be positive.", 400);
        }
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                update goals set title = $2, description = $3, metric_key = $4, target_value = $5,
                    target_date = $6, status = $7, constraints = $8::jsonb, updated_at = $10,
                    version = version + 1
                where id = $1 and version = $11
                """;
            AddGoalParameters(update, command.GoalId, command.Title, command.Description,
                command.MetricKey, command.TargetValue, command.TargetDate, command.Status,
                command.Constraints, command.OwnerUserId, now);
            update.Parameters.AddWithValue(command.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new OwnerConsoleException("goal_version_conflict", "The goal changed before this update.", 409);
            }
        }
        var result = await LoadGoalAsync(connection, command.GoalId, cancellationToken)
            ?? throw new OwnerConsoleException("goal_not_found", "The goal was not found.", 404);
        await WriteAuditAsync(command.OwnerUserId, "goal.updated", "goal", command.GoalId, null,
            command.CorrelationId, "Owner updated a goal.", cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<OwnerSystemControlRecord>> ListSystemControlsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, null, cancellationToken);
        var controls = await LoadSystemControlsAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return controls;
    }

    public async Task<OwnerSystemControlRecord> UpdateSystemControlAsync(
        UpdateSystemControlCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.OwnerUserId == Guid.Empty || command.ExpectedVersion <= 0 ||
            !string.Equals(command.Confirmation, "CONFIRM F0 CONTROL CHANGE", StringComparison.Ordinal))
        {
            throw new OwnerConsoleException(
                "control_confirmation_required",
                "The exact F0 control-change confirmation is required.",
                400);
        }
        if (LockedF0Controls.TryGetValue(command.ControlKey, out var required) && command.Enabled != required)
        {
            throw new OwnerConsoleException(
                "control_locked_for_f0",
                "This control is locked to the safe Foundation Release F0 value.",
                409);
        }

        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, null, cancellationToken);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update system_controls set enabled = $2, value = to_jsonb($2::boolean),
                    updated_by_user_id = $3, updated_at = $4, version = version + 1
                where control_key = $1 and version = $5
                """;
            update.Parameters.AddWithValue(command.ControlKey);
            update.Parameters.AddWithValue(command.Enabled);
            update.Parameters.AddWithValue(command.OwnerUserId);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(command.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new OwnerConsoleException("control_version_conflict", "The control changed before this update.", 409);
            }
        }
        var controls = await LoadSystemControlsAsync(connection, transaction, cancellationToken);
        var result = controls.SingleOrDefault(control => control.ControlKey == command.ControlKey)
            ?? throw new OwnerConsoleException("control_not_found", "The system control was not found.", 404);
        await transaction.CommitAsync(cancellationToken);
        await WriteAuditAsync(command.OwnerUserId, "system_control.updated", "system_control", null, null,
            command.CorrelationId, $"Owner set {command.ControlKey} to {command.Enabled}.", cancellationToken,
            command.ControlKey);
        return result;
    }

    private async Task<IReadOnlyList<OwnerWorkflowRecord>> LoadWorkflowsPublicAsync(
        Guid organizationId, Guid? workflowRunId, CancellationToken cancellationToken)
    {
        ValidateOrganization(organizationId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, organizationId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, organization_id, workflow_type, workflow_id, temporal_run_id, task_id,
                status, input::text, result::text, started_at, completed_at, updated_at
            from workflow_runs
            where ($1::uuid is null or id = $1)
            order by started_at desc limit 250
            """;
        command.Parameters.AddWithValue((object?)workflowRunId ?? DBNull.Value);
        var records = new List<OwnerWorkflowRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new OwnerWorkflowRecord(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                NullableString(reader, 4), NullableGuid(reader, 5), reader.GetString(6),
                ParseJson(reader.GetString(7)), reader.IsDBNull(8) ? null : ParseJson(reader.GetString(8)),
                reader.GetFieldValue<DateTimeOffset>(9), NullableDateTimeOffset(reader, 10),
                reader.GetFieldValue<DateTimeOffset>(11)));
        }
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return records;
    }

    private async Task<IReadOnlyList<OwnerArtifactRecord>> LoadArtifactsPublicAsync(
        Guid organizationId, Guid? artifactId, CancellationToken cancellationToken)
    {
        ValidateOrganization(organizationId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetOwnerContextAsync(connection, transaction, organizationId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, organization_id, artifact_type, title, content_type, inline_content::text,
                sha256, sensitivity, created_by_type, created_by_id, created_at, supersedes_artifact_id
            from artifacts
            where ($1::uuid is null or id = $1)
            order by created_at desc limit 250
            """;
        command.Parameters.AddWithValue((object?)artifactId ?? DBNull.Value);
        var records = new List<OwnerArtifactRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new OwnerArtifactRecord(
                reader.GetGuid(0), NullableGuid(reader, 1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : ParseJson(reader.GetString(5)),
                reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
                reader.GetFieldValue<DateTimeOffset>(10), NullableGuid(reader, 11)));
        }
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return records;
    }

    private static async Task<IReadOnlyList<OwnerAgentDefinitionRecord>> LoadAgentsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string? agentKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select definition.agent_key, definition.display_name, definition.description, definition.status,
                version.version_number, version.prompt_version, version.model_key, version.tool_permissions::text,
                count(run.id)::int, count(run.id) filter (where run.status = 'failed')::int,
                coalesce(sum(run.input_tokens), 0)::bigint, coalesce(sum(run.output_tokens), 0)::bigint,
                max(run.started_at)
            from agent_definitions definition
            join agent_versions version on version.id = definition.active_version_id
            left join agent_runs run on run.agent_version_id = version.id
            where ($1::text is null or definition.agent_key = $1)
            group by definition.agent_key, definition.display_name, definition.description,
                definition.status, version.version_number, version.prompt_version, version.model_key,
                version.tool_permissions
            order by definition.agent_key
            """;
        command.Parameters.AddWithValue((object?)agentKey ?? DBNull.Value);
        var agents = new List<OwnerAgentDefinitionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            agents.Add(new OwnerAgentDefinitionRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetString(6), ParseStringArray(reader.GetString(7)),
                reader.GetInt32(8), reader.GetInt32(9), reader.GetInt64(10), reader.GetInt64(11),
                NullableDateTimeOffset(reader, 12)));
        }
        return agents;
    }

    private static async Task<IReadOnlyList<OwnerAgentRunRecord>> LoadAgentRunsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid? runId, int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select run.id, run.organization_id, run.task_id, definition.agent_key, run.status,
                run.model_name, run.prompt_version, run.output_artifact_id, run.input_tokens,
                run.output_tokens, run.started_at, run.completed_at, run.failure_code, run.failure_message
            from agent_runs run
            join agent_versions version on version.id = run.agent_version_id
            join agent_definitions definition on definition.id = version.agent_definition_id
            where ($1::uuid is null or run.id = $1)
            order by run.started_at desc limit $2
            """;
        command.Parameters.AddWithValue((object?)runId ?? DBNull.Value);
        command.Parameters.AddWithValue(limit);
        var records = new List<OwnerAgentRunRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new OwnerAgentRunRecord(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), NullableGuid(reader, 7),
                reader.GetInt64(8), reader.GetInt64(9), reader.GetFieldValue<DateTimeOffset>(10),
                NullableDateTimeOffset(reader, 11), NullableString(reader, 12), NullableString(reader, 13)));
        }
        return records;
    }

    private static async Task<IReadOnlyList<OwnerSystemControlRecord>> LoadSystemControlsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select control_key, enabled, value::text, updated_by_user_id, updated_at, version
            from system_controls order by control_key
            """;
        var controls = new List<OwnerSystemControlRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = reader.GetString(0);
            controls.Add(new OwnerSystemControlRecord(
                key, reader.GetBoolean(1), ParseJson(reader.GetString(2)), reader.GetGuid(3),
                reader.GetFieldValue<DateTimeOffset>(4), reader.GetInt32(5), LockedF0Controls.ContainsKey(key)));
        }
        return controls;
    }

    private static async Task<OwnerTaskRecord?> LoadTaskAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid taskId, bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = TaskSelect + "\nwhere task.id = $1" + (forUpdate ? " for update" : string.Empty);
        command.Parameters.AddWithValue(taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapTask(reader) : null;
    }

    private static OwnerTaskRecord MapTask(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), NullableGuid(reader, 1), NullableGuid(reader, 2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
        NullableString(reader, 8), ParseJson(reader.GetString(9)), ParseJson(reader.GetString(10)),
        NullableGuid(reader, 11), NullableString(reader, 12), NullableString(reader, 13),
        reader.GetFieldValue<DateTimeOffset>(14), NullableDateTimeOffset(reader, 15),
        NullableDateTimeOffset(reader, 16), reader.GetFieldValue<DateTimeOffset>(17), reader.GetInt32(18));

    private static async Task<OwnerGoalRecord?> LoadGoalAsync(
        NpgsqlConnection connection, Guid goalId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = GoalSelect + "\nwhere id = $1";
        command.Parameters.AddWithValue(goalId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapGoal(reader) : null;
    }

    private static OwnerGoalRecord MapGoal(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), NullableString(reader, 3),
        reader.IsDBNull(4) ? null : reader.GetDecimal(4), NullableDateTimeOffset(reader, 5),
        reader.GetString(6), ParseJson(reader.GetString(7)), reader.GetFieldValue<DateTimeOffset>(8),
        reader.GetFieldValue<DateTimeOffset>(9), reader.GetInt32(10));

    private static void AddGoalParameters(
        NpgsqlCommand command, Guid id, string title, string description, string? metricKey,
        decimal? targetValue, DateTimeOffset? targetDate, string status, JsonElement constraints,
        Guid ownerUserId, DateTimeOffset now)
    {
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(title.Trim());
        command.Parameters.AddWithValue(description.Trim());
        command.Parameters.AddWithValue((object?)NormalizeFilter(metricKey) ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)targetValue ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)targetDate ?? DBNull.Value);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(CanonicalJson.Normalize(constraints));
        command.Parameters.AddWithValue(ownerUserId);
        command.Parameters.AddWithValue(now);
    }

    private static async Task<int> ScalarIntAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Owner dashboard count was null."));
    }

    private static async Task<bool> VerifyAuditLinksAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select coalesce(bool_and(chain_link_valid), true)
            from (
                select previous_hash is not distinct from lag(event_hash) over (order by sequence_number) as chain_link_valid
                from audit_events
            ) links
            """;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static Task SetOwnerContextAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid? organizationId,
        CancellationToken cancellationToken) =>
        PostgresToolGatewayService.SetTenantContextAsync(connection, transaction, organizationId, true, cancellationToken);

    private async Task WriteAuditAsync(
        Guid ownerUserId, string action, string targetType, Guid? targetId, Guid? organizationId,
        string correlationId, string summary, CancellationToken cancellationToken, string? textualTargetId = null) =>
        await auditWriter.AppendAsync(new AuditEventWrite(
            "user", ownerUserId.ToString(), action, targetType,
            textualTargetId ?? targetId?.ToString(), organizationId, correlationId,
            null, summary, "{}", timeProvider.GetUtcNow()), cancellationToken);

    private static void ValidateOrganization(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new OwnerConsoleException("organization_required", "Organization context is required.", 400);
    }

    private static void ValidateCreateTask(CreateOwnerTaskCommand command)
    {
        ValidateOrganization(command.OrganizationId);
        if (command.OwnerUserId == Guid.Empty || string.IsNullOrWhiteSpace(command.Type) || command.Type.Length > 100 ||
            string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 200 ||
            string.IsNullOrWhiteSpace(command.Description) || command.Description.Length > 5000 ||
            string.IsNullOrWhiteSpace(command.Priority) || command.Priority.Length > 50 ||
            command.Input.ValueKind != JsonValueKind.Object || command.Constraints.ValueKind != JsonValueKind.Object ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 200 ||
            (command.AssignedAgentKey is not null && !AgentKeys.Contains(command.AssignedAgentKey)))
        {
            throw new OwnerConsoleException("invalid_owner_task", "The owner task request is invalid.", 400);
        }
        if (command.AssignedAgentKey == "engineering" && command.Type != "engineering")
        {
            throw new OwnerConsoleException("engineering_task_type_required", "Engineering Agent requires task type engineering.", 400);
        }
    }

    private static void ValidateGoal(string title, string description, string status, JsonElement constraints)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 ||
            string.IsNullOrWhiteSpace(description) || description.Length > 5000 ||
            !GoalStatuses.Contains(status) || constraints.ValueKind != JsonValueKind.Object)
        {
            throw new OwnerConsoleException("invalid_goal", "The goal request is invalid.", 400);
        }
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
    }

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset? NullableDateTimeOffset(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private const string TaskSelect = """
        select task.id, task.organization_id, task.goal_id, task.type, task.title, task.description,
            task.priority, task.status, task.assigned_agent_key, task.input::text, task.constraints::text,
            task.result_artifact_id, task.error_code, task.error_message, task.created_at, task.started_at,
            task.completed_at, task.updated_at, task.version
        from tasks task
        """;

    private const string GoalSelect = """
        select id, title, description, metric_key, target_value, target_date, status,
            constraints::text, created_at, updated_at, version
        from goals
        """;
}
