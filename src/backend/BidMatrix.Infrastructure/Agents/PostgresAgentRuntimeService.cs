using System.Data;
using System.Text.Json;
using BidMatrix.Application.Agents;
using BidMatrix.Application.Audit;
using BidMatrix.Application.Tools;
using BidMatrix.Infrastructure.Tools;
using Npgsql;

namespace BidMatrix.Infrastructure.Agents;

public sealed class PostgresAgentRuntimeService(
    NpgsqlDataSource dataSource,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IAgentRuntimeService
{
    private static readonly HashSet<string> AgentKeys = new(StringComparer.Ordinal)
    {
        "executive",
        "support",
        "product-analyst",
        "engineering",
    };

    public async Task<AgentDemoTaskRecord> CreateDemoAsync(
        AgentDemoCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCreate(command);
        var input = command.Input ?? AgentDemoFixtures.Get(command.AgentKey);
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new ToolGatewayException("invalid_agent_input", "Agent input must be a JSON object.", 400);
        }

        var normalizedInput = CanonicalJson.Normalize(input);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(
            connection,
            transaction,
            command.OrganizationId,
            true,
            cancellationToken);

        var existing = await LoadTaskByIdempotencyAsync(
            connection,
            transaction,
            command.OrganizationId,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        var taskId = Guid.CreateVersion7();
        var workflowId = $"agent-task-{taskId}";
        await using (var task = connection.CreateCommand())
        {
            task.Transaction = transaction;
            task.CommandText = """
                insert into tasks (
                    id, organization_id, type, title, description, priority, status,
                    assigned_agent_key, input, constraints, created_by_type, created_by_id,
                    created_at, updated_at, version, idempotency_key
                )
                values (
                    $1, $2, $3, $4, $5, 'normal', 'queued',
                    $6, $7::jsonb, $8::jsonb, 'user', $9,
                    $10, $10, 1, $11
                )
                """;
            task.Parameters.AddWithValue(taskId);
            task.Parameters.AddWithValue(command.OrganizationId);
            task.Parameters.AddWithValue(ToTaskType(command.AgentKey));
            task.Parameters.AddWithValue($"{ToDisplayName(command.AgentKey)} offline demonstration");
            task.Parameters.AddWithValue("F0 deterministic offline agent demonstration.");
            task.Parameters.AddWithValue(command.AgentKey);
            task.Parameters.AddWithValue(normalizedInput);
            task.Parameters.AddWithValue(JsonSerializer.Serialize(new
            {
                fixture = command.Input is null,
                externalActionsEnabled = false,
                liveModelOptInRequired = true,
            }));
            task.Parameters.AddWithValue(command.OwnerUserId.ToString());
            task.Parameters.AddWithValue(now);
            task.Parameters.AddWithValue(command.IdempotencyKey);
            await task.ExecuteNonQueryAsync(cancellationToken);
        }

        var eventPayload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            organizationId = command.OrganizationId,
            correlationId = command.CorrelationId,
            payload = new
            {
                taskId,
                agentKey = command.AgentKey,
                workflowId,
            },
        });
        await using (var outbox = connection.CreateCommand())
        {
            outbox.Transaction = transaction;
            outbox.CommandText = """
                insert into outbox_events (
                    id, event_type, aggregate_type, aggregate_id, payload,
                    occurred_at, available_at, attempt_count
                )
                values ($1, 'agent.task.created.v1', 'task', $2, $3::jsonb, $4, $4, 0)
                """;
            outbox.Parameters.AddWithValue(Guid.CreateVersion7());
            outbox.Parameters.AddWithValue(taskId);
            outbox.Parameters.AddWithValue(eventPayload);
            outbox.Parameters.AddWithValue(now);
            await outbox.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var result = new AgentDemoTaskRecord(
            taskId,
            command.OrganizationId,
            command.AgentKey,
            "queued",
            workflowId,
            now);
        await auditWriter.AppendAsync(new AuditEventWrite(
            "user",
            command.OwnerUserId.ToString(),
            "agent_demo.created",
            "task",
            taskId.ToString(),
            command.OrganizationId,
            command.CorrelationId,
            command.CorrelationId,
            $"Created {command.AgentKey} offline demonstration task.",
            JsonSerializer.Serialize(new { command.AgentKey, workflowId }),
            now), cancellationToken);
        return result;
    }

    public async Task<AgentTaskPreparation> PrepareAsync(
        Guid organizationId,
        Guid taskId,
        string workflowId,
        string correlationId,
        string runtimeMode,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        ValidatePreparation(organizationId, taskId, workflowId, correlationId, runtimeMode, modelName);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(
            connection,
            transaction,
            organizationId,
            false,
            cancellationToken);

        var task = await LoadAssignedTaskAsync(connection, transaction, taskId, cancellationToken);
        var existing = await LoadPreparationAsync(
            connection,
            transaction,
            taskId,
            workflowId,
            correlationId,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        var agent = await LoadActiveAgentAsync(
            connection,
            transaction,
            task.AgentKey,
            cancellationToken);
        var workflowRunId = Guid.CreateVersion7();
        await using (var workflow = connection.CreateCommand())
        {
            workflow.Transaction = transaction;
            workflow.CommandText = """
                insert into workflow_runs (
                    id, organization_id, workflow_type, workflow_id, task_id, status,
                    input, started_at, updated_at
                )
                values ($1, $2, 'AgentTaskWorkflow', $3, $4, 'running', $5::jsonb, $6, $6)
                on conflict (workflow_id) do nothing
                """;
            workflow.Parameters.AddWithValue(workflowRunId);
            workflow.Parameters.AddWithValue(organizationId);
            workflow.Parameters.AddWithValue(workflowId);
            workflow.Parameters.AddWithValue(taskId);
            workflow.Parameters.AddWithValue(task.InputJson);
            workflow.Parameters.AddWithValue(now);
            await workflow.ExecuteNonQueryAsync(cancellationToken);
        }

        var agentRunId = Guid.CreateVersion7();
        await using (var run = connection.CreateCommand())
        {
            run.Transaction = transaction;
            run.CommandText = """
                insert into agent_runs (
                    id, organization_id, workflow_run_id, task_id, agent_version_id, status,
                    model_name, prompt_version, trace_id, input_hash, started_at
                )
                select $1, $2, workflow.id, $3, $4, 'running',
                       $5, $6, $7, $8, $9
                from workflow_runs workflow
                where workflow.workflow_id = $10
                """;
            run.Parameters.AddWithValue(agentRunId);
            run.Parameters.AddWithValue(organizationId);
            run.Parameters.AddWithValue(taskId);
            run.Parameters.AddWithValue(agent.VersionId);
            run.Parameters.AddWithValue(modelName);
            run.Parameters.AddWithValue(agent.PromptVersion);
            run.Parameters.AddWithValue(correlationId);
            run.Parameters.AddWithValue(CanonicalJson.HashNormalized(task.InputJson));
            run.Parameters.AddWithValue(now);
            run.Parameters.AddWithValue(workflowId);
            await run.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update tasks
                set status = 'running', started_at = coalesce(started_at, $2), updated_at = $2, version = version + 1
                where id = $1 and status in ('queued', 'assigned', 'running')
                """;
            update.Parameters.AddWithValue(taskId);
            update.Parameters.AddWithValue(now);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new ToolGatewayException(
                    "agent_task_not_runnable",
                    "The task is not in a runnable state.",
                    409);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new AgentTaskPreparation(
            taskId,
            organizationId,
            agentRunId,
            task.AgentKey,
            agent.VersionNumber,
            modelName,
            agent.PromptVersion,
            ParseStringArray(agent.ToolPermissionsJson),
            CanonicalJson.ParseNormalized(task.InputJson),
            workflowId,
            correlationId);
    }

    public async Task<AgentRunRecord> CompleteAsync(
        CompleteAgentRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCompletion(command);
        var normalizedOutput = CanonicalJson.Normalize(command.Output);
        var outputHash = CanonicalJson.HashNormalized(normalizedOutput);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(
            connection,
            transaction,
            command.OrganizationId,
            false,
            cancellationToken);

        await using (var artifact = connection.CreateCommand())
        {
            artifact.Transaction = transaction;
            artifact.CommandText = """
                select count(*)
                from artifacts
                where id = $1 and organization_id = $2 and created_by_id = $3
                """;
            artifact.Parameters.AddWithValue(command.OutputArtifactId);
            artifact.Parameters.AddWithValue(command.OrganizationId);
            artifact.Parameters.AddWithValue(command.AgentRunId.ToString());
            if (Convert.ToInt32(await artifact.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                throw new ToolGatewayException(
                    "invalid_output_artifact",
                    "The output artifact was not created through the active agent run.",
                    409);
            }
        }

        await using (var run = connection.CreateCommand())
        {
            run.Transaction = transaction;
            run.CommandText = """
                update agent_runs
                set status = 'completed',
                    output_artifact_id = $4,
                    output_hash = $5,
                    request_count = $6,
                    input_tokens = $7,
                    output_tokens = $8,
                    reasoning_tokens = $9,
                    completed_at = $10
                where id = $1 and task_id = $2 and organization_id = $3 and status = 'running'
                """;
            run.Parameters.AddWithValue(command.AgentRunId);
            run.Parameters.AddWithValue(command.TaskId);
            run.Parameters.AddWithValue(command.OrganizationId);
            run.Parameters.AddWithValue(command.OutputArtifactId);
            run.Parameters.AddWithValue(outputHash);
            run.Parameters.AddWithValue(command.RequestCount);
            run.Parameters.AddWithValue(command.InputTokens);
            run.Parameters.AddWithValue(command.OutputTokens);
            run.Parameters.AddWithValue((object?)command.ReasoningTokens ?? DBNull.Value);
            run.Parameters.AddWithValue(now);
            if (await run.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new ToolGatewayException(
                    "agent_run_not_active",
                    "The agent run is not active or does not match the task.",
                    409);
            }
        }

        await using (var task = connection.CreateCommand())
        {
            task.Transaction = transaction;
            task.CommandText = """
                update tasks
                set status = 'completed', result_artifact_id = $2, completed_at = $3,
                    updated_at = $3, version = version + 1
                where id = $1 and status = 'running'
                """;
            task.Parameters.AddWithValue(command.TaskId);
            task.Parameters.AddWithValue(command.OutputArtifactId);
            task.Parameters.AddWithValue(now);
            await task.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var workflow = connection.CreateCommand())
        {
            workflow.Transaction = transaction;
            workflow.CommandText = """
                update workflow_runs
                set status = 'completed', result = $2::jsonb, completed_at = $3, updated_at = $3
                where task_id = $1 and status = 'running'
                """;
            workflow.Parameters.AddWithValue(command.TaskId);
            workflow.Parameters.AddWithValue(JsonSerializer.Serialize(new
            {
                command.AgentRunId,
                command.OutputArtifactId,
                outputHash,
            }));
            workflow.Parameters.AddWithValue(now);
            await workflow.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var result = await GetRunAsync(command.AgentRunId, command.OrganizationId, cancellationToken)
            ?? throw new InvalidOperationException("Agent run disappeared after completion.");
        await auditWriter.AppendAsync(new AuditEventWrite(
            "agent_run",
            command.AgentRunId.ToString(),
            "agent_run.completed",
            "task",
            command.TaskId.ToString(),
            command.OrganizationId,
            command.CorrelationId,
            command.CorrelationId,
            "Agent run completed with validated structured output.",
            JsonSerializer.Serialize(new
            {
                command.OutputArtifactId,
                outputHash,
                command.RequestCount,
                command.InputTokens,
                command.OutputTokens,
            }),
            now), cancellationToken);
        return result;
    }

    public async Task<AgentRunRecord?> FailAsync(
        FailAgentRunCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TaskId == Guid.Empty ||
            command.OrganizationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.FailureCode) ||
            command.FailureCode.Length > 200 ||
            string.IsNullOrWhiteSpace(command.FailureMessage) ||
            command.FailureMessage.Length > 2_000)
        {
            throw new ToolGatewayException("invalid_agent_failure", "Agent failure data is invalid.", 400);
        }

        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(
            connection,
            transaction,
            command.OrganizationId,
            false,
            cancellationToken);
        Guid? agentRunId = command.AgentRunId;
        if (agentRunId is null)
        {
            await using var find = connection.CreateCommand();
            find.Transaction = transaction;
            find.CommandText = "select id from agent_runs where task_id = $1";
            find.Parameters.AddWithValue(command.TaskId);
            agentRunId = await find.ExecuteScalarAsync(cancellationToken) as Guid?;
        }

        if (agentRunId is not null)
        {
            await using var run = connection.CreateCommand();
            run.Transaction = transaction;
            run.CommandText = """
                update agent_runs
                set status = 'failed', failure_code = $2, failure_message = $3, completed_at = $4
                where id = $1 and status <> 'completed'
                """;
            run.Parameters.AddWithValue(agentRunId.Value);
            run.Parameters.AddWithValue(command.FailureCode);
            run.Parameters.AddWithValue(command.FailureMessage);
            run.Parameters.AddWithValue(now);
            await run.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var task = connection.CreateCommand())
        {
            task.Transaction = transaction;
            task.CommandText = """
                update tasks
                set status = 'failed', error_code = $2, error_message = $3,
                    completed_at = $4, updated_at = $4, version = version + 1
                where id = $1 and status <> 'completed'
                """;
            task.Parameters.AddWithValue(command.TaskId);
            task.Parameters.AddWithValue(command.FailureCode);
            task.Parameters.AddWithValue(command.FailureMessage);
            task.Parameters.AddWithValue(now);
            await task.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var workflow = connection.CreateCommand())
        {
            workflow.Transaction = transaction;
            workflow.CommandText = """
                update workflow_runs
                set status = 'failed', result = $2::jsonb, completed_at = $3, updated_at = $3
                where task_id = $1 and status <> 'completed'
                """;
            workflow.Parameters.AddWithValue(command.TaskId);
            workflow.Parameters.AddWithValue(JsonSerializer.Serialize(new
            {
                command.FailureCode,
                command.FailureMessage,
            }));
            workflow.Parameters.AddWithValue(now);
            await workflow.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await auditWriter.AppendAsync(new AuditEventWrite(
            "system",
            "agent-task-workflow",
            "agent_run.failed",
            "task",
            command.TaskId.ToString(),
            command.OrganizationId,
            command.CorrelationId,
            command.CorrelationId,
            "Agent run failed and the task was not completed.",
            JsonSerializer.Serialize(new { command.FailureCode }),
            now), cancellationToken);
        return agentRunId is null
            ? null
            : await GetRunAsync(agentRunId.Value, command.OrganizationId, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRunRecord>> ListRunsAsync(
        Guid? organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(
            connection,
            transaction,
            organizationId,
            true,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RunSelect + "\n" + """
            where ($1::uuid is null or run.organization_id = $1)
            order by run.started_at desc
            limit 250
            """;
        command.Parameters.AddWithValue((object?)organizationId ?? DBNull.Value);
        var results = new List<AgentRunRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapRun(reader));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    private async Task<AgentRunRecord?> GetRunAsync(
        Guid agentRunId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(
            connection,
            transaction,
            organizationId,
            false,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RunSelect + "\nwhere run.id = $1";
        command.Parameters.AddWithValue(agentRunId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = await reader.ReadAsync(cancellationToken) ? MapRun(reader) : null;
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<AgentDemoTaskRecord?> LoadTaskByIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, organization_id, assigned_agent_key, status, created_at
            from tasks
            where organization_id = $1 and idempotency_key = $2
            """;
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AgentDemoTaskRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                $"agent-task-{reader.GetGuid(0)}",
                reader.GetFieldValue<DateTimeOffset>(4))
            : null;
    }

    private static async Task<AssignedTask> LoadAssignedTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select assigned_agent_key, input::text from tasks where id = $1";
        command.Parameters.AddWithValue(taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
        {
            throw new ToolGatewayException(
                "agent_task_not_found",
                "The assigned agent task was not found in this organization.",
                404);
        }

        return new AssignedTask(reader.GetString(0), reader.GetString(1));
    }

    private static async Task<ActiveAgent> LoadActiveAgentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string agentKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select version.id, version.version_number, version.prompt_version, version.tool_permissions::text
            from agent_definitions definition
            join agent_versions version on version.id = definition.active_version_id
            where definition.agent_key = $1 and definition.status = 'active'
            """;
        command.Parameters.AddWithValue(agentKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ActiveAgent(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3))
            : throw new ToolGatewayException("agent_not_active", "The assigned agent is not active.", 409);
    }

    private static async Task<AgentTaskPreparation?> LoadPreparationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        string workflowId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select
                run.id, run.organization_id, definition.agent_key, version.version_number,
                run.model_name, run.prompt_version, version.tool_permissions::text,
                task.input::text
            from agent_runs run
            join tasks task on task.id = run.task_id
            join agent_versions version on version.id = run.agent_version_id
            join agent_definitions definition on definition.id = version.agent_definition_id
            where run.task_id = $1
            """;
        command.Parameters.AddWithValue(taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AgentTaskPreparation(
                taskId,
                reader.GetGuid(1),
                reader.GetGuid(0),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                ParseStringArray(reader.GetString(6)),
                CanonicalJson.ParseNormalized(reader.GetString(7)),
                workflowId,
                correlationId)
            : null;
    }

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Select(item => item.GetString() ?? throw new JsonException("Tool permission must be a string."))
            .ToArray();
    }

    private static AgentRunRecord MapRun(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetGuid(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetGuid(7),
        reader.GetInt64(8),
        reader.GetInt64(9),
        reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.IsDBNull(13) ? null : reader.GetString(13));

    private static void ValidateCreate(AgentDemoCreateCommand command)
    {
        if (command.OrganizationId == Guid.Empty ||
            command.OwnerUserId == Guid.Empty ||
            !AgentKeys.Contains(command.AgentKey) ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Length > 500 ||
            string.IsNullOrWhiteSpace(command.CorrelationId) ||
            command.CorrelationId.Length > 200)
        {
            throw new ToolGatewayException("invalid_agent_demo", "Agent demo request is invalid.", 400);
        }
    }

    private static void ValidatePreparation(
        Guid organizationId,
        Guid taskId,
        string workflowId,
        string correlationId,
        string runtimeMode,
        string modelName)
    {
        if (organizationId == Guid.Empty ||
            taskId == Guid.Empty ||
            string.IsNullOrWhiteSpace(workflowId) ||
            workflowId.Length > 300 ||
            string.IsNullOrWhiteSpace(correlationId) ||
            correlationId.Length > 200 ||
            runtimeMode is not ("deterministic" or "live") ||
            string.IsNullOrWhiteSpace(modelName) ||
            modelName.Length > 200)
        {
            throw new ToolGatewayException("invalid_agent_preparation", "Agent preparation request is invalid.", 400);
        }
    }

    private static void ValidateCompletion(CompleteAgentRunCommand command)
    {
        if (command.TaskId == Guid.Empty ||
            command.OrganizationId == Guid.Empty ||
            command.AgentRunId == Guid.Empty ||
            command.OutputArtifactId == Guid.Empty ||
            command.Output.ValueKind != JsonValueKind.Object ||
            command.RequestCount < 0 ||
            command.InputTokens < 0 ||
            command.OutputTokens < 0 ||
            command.ReasoningTokens < 0)
        {
            throw new ToolGatewayException("invalid_agent_completion", "Agent completion data is invalid.", 400);
        }
    }

    private static string ToTaskType(string agentKey) => agentKey switch
    {
        "product-analyst" => "product-review",
        _ => agentKey,
    };

    private static string ToDisplayName(string agentKey) => agentKey switch
    {
        "product-analyst" => "Product Analyst",
        _ => char.ToUpperInvariant(agentKey[0]) + agentKey[1..],
    };

    private const string RunSelect = """
        select
            run.id,
            run.organization_id,
            run.task_id,
            definition.agent_key,
            run.status,
            run.model_name,
            run.prompt_version,
            run.output_artifact_id,
            run.input_tokens,
            run.output_tokens,
            run.started_at,
            run.completed_at,
            run.failure_code,
            run.failure_message
        from agent_runs run
        join agent_versions version on version.id = run.agent_version_id
        join agent_definitions definition on definition.id = version.agent_definition_id
        """;

    private sealed record AssignedTask(string AgentKey, string InputJson);
    private sealed record ActiveAgent(
        Guid VersionId,
        int VersionNumber,
        string PromptVersion,
        string ToolPermissionsJson);
}
