using System.Data;
using System.Text.Json;
using BidMatrix.Application.Audit;
using BidMatrix.Application.Engineering;
using BidMatrix.Application.Tools;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BidMatrix.Infrastructure.Tools;

public sealed class PostgresToolGatewayService(
    NpgsqlDataSource dataSource,
    IPolicyEngine policyEngine,
    IAuditWriter auditWriter,
    IEngineeringSandboxService engineeringSandbox,
    IHostEnvironment environment,
    TimeProvider timeProvider) : IToolGatewayService
{
    public async Task<ToolGatewayResult> ExecuteAsync(
        ToolGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        ToolArgumentValidator.Validate(request.ToolKey, request.Arguments);

        var normalizedInput = CanonicalJson.Normalize(request.Arguments);
        var inputHash = CanonicalJson.HashNormalized(normalizedInput);
        var now = timeProvider.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await SetTenantContextAsync(connection, transaction, request.OrganizationId, false, cancellationToken);

        var definition = await LoadToolDefinitionAsync(
            connection,
            transaction,
            request.ToolKey,
            cancellationToken);
        var agentContext = await LoadAgentContextAsync(
            connection,
            transaction,
            request,
            cancellationToken);
        EnsurePermission(agentContext.ToolPermissionsJson, request.ToolKey);

        var policy = await LoadPolicyAsync(connection, transaction, cancellationToken);
        var controls = await LoadControlsAsync(connection, transaction, cancellationToken);
        var evaluation = policyEngine.Evaluate(new PolicyEvaluationContext(
            definition,
            request.AgentKey,
            agentContext.TaskType,
            agentContext.OwnerCreatedTask,
            controls,
            environment.EnvironmentName));

        var existing = await LoadExistingAsync(
            connection,
            transaction,
            request.OrganizationId,
            definition.Id,
            request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.InputHash, inputHash, StringComparison.Ordinal))
            {
                throw new ToolGatewayException(
                    "idempotency_payload_mismatch",
                    "The idempotency key was already used for a different normalized payload.",
                    409);
            }

            await transaction.CommitAsync(cancellationToken);
            var replay = new ToolGatewayResult(
                ToolDecisions.AlreadyExecuted,
                existing.ToolCallId,
                policy.DisplayVersion,
                existing.ApprovalId,
                "idempotent_replay",
                inputHash,
                existing.ExecutionStatus,
                existing.Output);
            await AuditDecisionAsync(request, replay, now, cancellationToken);
            return replay;
        }

        var toolCallId = Guid.CreateVersion7();
        await InsertToolCallAsync(
            connection,
            transaction,
            toolCallId,
            request,
            definition.Id,
            policy.VersionId,
            normalizedInput,
            inputHash,
            evaluation,
            now,
            cancellationToken);

        Guid? approvalId = null;
        JsonElement? output = null;
        var executionStatus = "notStarted";

        switch (evaluation.Decision)
        {
            case ToolDecisions.Allowed:
                var execution = await ExecuteAllowedToolAsync(
                    connection,
                    transaction,
                    toolCallId,
                    request,
                    definition,
                    policy,
                    now,
                    cancellationToken);
                output = execution.Output;
                approvalId = execution.ApprovalId;
                executionStatus = "completed";
                await CompleteToolCallAsync(
                    connection,
                    transaction,
                    toolCallId,
                    evaluation.Decision,
                    evaluation.ReasonCode,
                    executionStatus,
                    output,
                    approvalId,
                    now,
                    cancellationToken);
                break;
            case ToolDecisions.ApprovalRequired:
                approvalId = await CreateApprovalAsync(
                    connection,
                    transaction,
                    toolCallId,
                    request,
                    definition,
                    policy,
                    request.Arguments,
                    $"{request.AgentKey} proposes {request.ToolKey}.",
                    evaluation.TechnicallyEnabled,
                    now,
                    cancellationToken);
                executionStatus = "pending";
                await CompleteToolCallAsync(
                    connection,
                    transaction,
                    toolCallId,
                    evaluation.Decision,
                    evaluation.ReasonCode,
                    executionStatus,
                    null,
                    approvalId,
                    now,
                    cancellationToken);
                break;
            case ToolDecisions.Disabled:
                executionStatus = "disabled";
                output = JsonSerializer.SerializeToElement(new
                {
                    status = "disabled",
                    reason = "The adapter is technically disabled and no external action occurred in F1.",
                });
                await CompleteToolCallAsync(
                    connection,
                    transaction,
                    toolCallId,
                    evaluation.Decision,
                    evaluation.ReasonCode,
                    executionStatus,
                    output,
                    null,
                    now,
                    cancellationToken);
                break;
            default:
                await CompleteToolCallAsync(
                    connection,
                    transaction,
                    toolCallId,
                    evaluation.Decision,
                    evaluation.ReasonCode,
                    executionStatus,
                    null,
                    null,
                    now,
                    cancellationToken);
                break;
        }

        await transaction.CommitAsync(cancellationToken);

        var result = new ToolGatewayResult(
            evaluation.Decision,
            toolCallId,
            policy.DisplayVersion,
            approvalId,
            evaluation.ReasonCode,
            inputHash,
            executionStatus,
            output);
        await AuditDecisionAsync(request, result, now, cancellationToken);
        return result;
    }

    private async Task<ToolExecution> ExecuteAllowedToolAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid toolCallId,
        ToolGatewayRequest request,
        ToolDefinitionSnapshot definition,
        PolicyContext policy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return request.ToolKey switch
        {
            "context.getCompanyConstitution" => new ToolExecution(JsonSerializer.SerializeToElement(new
            {
                version = "f0-v1",
                draftOnly = true,
                externalEffectsEnabled = false,
                principles = new[] { "sourced-not-invented", "policy-before-action", "human-accountability" },
            }), null),
            "context.getProductFacts" => new ToolExecution(JsonSerializer.SerializeToElement(new
            {
                release = "F1",
                facts = new[]
                {
                    "BidMatrix extracts text from digital English PDF files and preserves page identity.",
                    "F1 produces sourced requirement candidates that always require human review.",
                    "OCR, scoring, legal conclusions, and external actions are unavailable in F1.",
                },
            }), null),
            "context.getTask" => new ToolExecution(
                await ReadTaskAsync(connection, transaction, request.Arguments, cancellationToken),
                null),
            "context.getAnalysis" => new ToolExecution(
                await ReadAnalysisAsync(connection, transaction, request.Arguments, cancellationToken),
                null),
            "context.getMetricsSnapshot" => new ToolExecution(
                await ReadMetricsAsync(connection, transaction, cancellationToken),
                null),
            "knowledge.search" => new ToolExecution(JsonSerializer.SerializeToElement(new
            {
                status = "noApprovedMatches",
                matches = Array.Empty<object>(),
                note = "The F1 knowledge index contains no approved searchable documents.",
            }), null),
            "artifact.read" => new ToolExecution(
                await ReadArtifactAsync(connection, transaction, request.Arguments, cancellationToken),
                null),
            "task.create" => new ToolExecution(
                await CreateTaskAsync(connection, transaction, request, now, cancellationToken),
                null),
            "task.addNote" => new ToolExecution(
                await CreateNoteArtifactAsync(connection, transaction, request, now, cancellationToken),
                null),
            "artifact.createDraft" => new ToolExecution(
                await CreateDraftArtifactAsync(connection, transaction, request, now, cancellationToken),
                null),
            "agentRun.addFinding" => new ToolExecution(
                await CreateFindingArtifactAsync(connection, transaction, request, now, cancellationToken),
                null),
            "repo.createWorktree" => new ToolExecution(
                await CreateEngineeringWorktreeAsync(connection, transaction, request, now, cancellationToken),
                null),
            "repo.readFile" => new ToolExecution(
                JsonSerializer.SerializeToElement(await engineeringSandbox.ReadFileAsync(
                    request.OrganizationId,
                    request.TaskId,
                    ToolArgumentValidator.RequireString(request.Arguments, "path", 500),
                    cancellationToken)),
                null),
            "repo.search" => new ToolExecution(
                JsonSerializer.SerializeToElement(new
                {
                    matches = await engineeringSandbox.SearchAsync(
                        request.OrganizationId,
                        request.TaskId,
                        ToolArgumentValidator.RequireString(request.Arguments, "query", 200),
                        ToolArgumentValidator.RequireOptionalString(request.Arguments, "path", 500),
                        cancellationToken),
                }),
                null),
            "repo.getStatus" => new ToolExecution(
                JsonSerializer.SerializeToElement(await engineeringSandbox.GetStatusAsync(
                    request.OrganizationId, request.TaskId, cancellationToken)),
                null),
            "repo.getDiff" => new ToolExecution(
                JsonSerializer.SerializeToElement(await engineeringSandbox.GetDiffAsync(
                    request.OrganizationId, request.TaskId, cancellationToken)),
                null),
            "repo.writeFile" => new ToolExecution(
                JsonSerializer.SerializeToElement(await engineeringSandbox.WriteFileAsync(
                    request.OrganizationId,
                    request.TaskId,
                    ToolArgumentValidator.RequireString(request.Arguments, "path", 500),
                    ToolArgumentValidator.RequireString(request.Arguments, "content", 262_144),
                    cancellationToken)),
                null),
            "repo.runAllowlistedCommand" => new ToolExecution(
                JsonSerializer.SerializeToElement(await engineeringSandbox.RunAllowlistedCommandAsync(
                    request.OrganizationId,
                    request.TaskId,
                    ToolArgumentValidator.RequireString(request.Arguments, "executable", 100),
                    ToolArgumentValidator.RequireStringArray(request.Arguments, "arguments", 20, 200),
                    cancellationToken)),
                null),
            "repo.createDiffArtifact" => new ToolExecution(
                await CreateEngineeringDiffArtifactAsync(
                    connection, transaction, request, now, cancellationToken),
                null),
            "approval.request" => await ExecuteApprovalRequestAsync(
                connection,
                transaction,
                toolCallId,
                request,
                definition,
                policy,
                now,
                cancellationToken),
            _ => throw new ToolGatewayException(
                "tool_adapter_unavailable",
                $"The {request.ToolKey} adapter is not available in this F1 runtime.",
                409),
        };
    }

    private async Task<JsonElement> CreateEngineeringWorktreeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolGatewayRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var result = await engineeringSandbox.CreateWorktreeAsync(
            request.OrganizationId,
            request.TaskId,
            ToolArgumentValidator.RequireString(request.Arguments, "baseRevision", 200),
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into engineering_sandboxes (
                id, organization_id, task_id, sandbox_key, base_revision, head_revision,
                status, created_at, updated_at, retain_until
            ) values ($1, $2, $3, $4, $5, $6, 'active', $7, $7, $8)
            on conflict (organization_id, task_id) do update
            set head_revision = excluded.head_revision,
                status = 'active',
                updated_at = excluded.updated_at,
                retain_until = excluded.retain_until
            """;
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(request.OrganizationId);
        command.Parameters.AddWithValue(request.TaskId);
        command.Parameters.AddWithValue($"{request.OrganizationId:N}/{request.TaskId:N}");
        command.Parameters.AddWithValue(result.BaseRevision);
        command.Parameters.AddWithValue(result.HeadRevision);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(now.AddDays(7));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private async Task<JsonElement> CreateEngineeringDiffArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolGatewayRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var diff = await engineeringSandbox.GetDiffAsync(request.OrganizationId, request.TaskId, cancellationToken);
        var title = ToolArgumentValidator.RequireOptionalString(request.Arguments, "title", 200)
            ?? "Engineering sandbox diff";
        var arguments = JsonSerializer.SerializeToElement(new
        {
            title,
            artifactType = "engineering_diff",
            content = new
            {
                diff = diff.Diff,
                sha256 = diff.Sha256,
                sizeBytes = diff.SizeBytes,
                remoteActionOccurred = false,
            },
        });
        var artifactRequest = request with { Arguments = arguments };
        return await CreateDraftArtifactAsync(
            connection, transaction, artifactRequest, now, cancellationToken);
    }

    private static async Task<ToolExecution> ExecuteApprovalRequestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid toolCallId,
        ToolGatewayRequest request,
        ToolDefinitionSnapshot definition,
        PolicyContext policy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var payload = request.Arguments.GetProperty("payload");
        var summary = ToolArgumentValidator.RequireString(request.Arguments, "summary", 500);
        var approvalId = await CreateApprovalAsync(
            connection,
            transaction,
            toolCallId,
            request,
            definition with
            {
                ToolKey = ToolArgumentValidator.RequireString(request.Arguments, "actionType", 100),
                RiskLevel = "yellow",
                Enabled = false,
            },
            policy,
            payload,
            summary,
            false,
            now,
            cancellationToken);
        return new ToolExecution(JsonSerializer.SerializeToElement(new
        {
            approvalId,
            status = "pending",
        }), approvalId);
    }

    private static async Task<Guid> CreateApprovalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid toolCallId,
        ToolGatewayRequest request,
        ToolDefinitionSnapshot definition,
        PolicyContext policy,
        JsonElement payload,
        string summary,
        bool technicallyEnabled,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var approvalId = Guid.CreateVersion7();
        var normalizedPayload = CanonicalJson.Normalize(payload);
        var payloadHash = CanonicalJson.HashNormalized(normalizedPayload);
        var expiresAt = now.AddHours(24);
        var presentation = JsonSerializer.Serialize(new
        {
            proposedAction = definition.ToolKey,
            target = FindPresentationValue(payload, "to", "target", "repository", "path"),
            requestingAgent = request.AgentKey,
            originatingTaskId = request.TaskId,
            reason = summary,
            supportingSources = FindPresentationElement(payload, "sources"),
            riskLevel = definition.RiskLevel,
            policyResult = "approvalRequired",
            expectedEffect = FindPresentationValue(payload, "expectedEffect"),
            idempotencyKey = request.IdempotencyKey,
            technicallyEnabled,
        });

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                insert into approvals (
                    id, organization_id, tool_call_id, task_id, action_type, status, summary,
                    normalized_payload, payload_hash, policy_version_id, requested_by_agent_run_id,
                    requested_at, expires_at, execution_status, version, presentation
                )
                values (
                    $1, $2, $3, $4, $5, 'pending', $6,
                    $7::jsonb, $8, $9, $10,
                    $11, $12, 'notStarted', 1, $13::jsonb
                )
                """;
            command.Parameters.AddWithValue(approvalId);
            command.Parameters.AddWithValue(request.OrganizationId);
            command.Parameters.AddWithValue(toolCallId);
            command.Parameters.AddWithValue(request.TaskId);
            command.Parameters.AddWithValue(definition.ToolKey);
            command.Parameters.AddWithValue(summary);
            command.Parameters.AddWithValue(normalizedPayload);
            command.Parameters.AddWithValue(payloadHash);
            command.Parameters.AddWithValue(policy.VersionId);
            command.Parameters.AddWithValue(request.AgentRunId);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(expiresAt);
            command.Parameters.AddWithValue(presentation);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var eventId = Guid.CreateVersion7();
        var eventPayload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            organizationId = request.OrganizationId,
            correlationId = request.CorrelationId,
            payload = new
            {
                approvalId,
                workflowId = $"approval-{approvalId}",
                expiresAt,
            },
        });
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                insert into outbox_events (
                    id, event_type, aggregate_type, aggregate_id, payload,
                    occurred_at, available_at, attempt_count
                )
                values ($1, 'approval.requested.v1', 'approval', $2, $3::jsonb, $4, $4, 0)
                """;
            command.Parameters.AddWithValue(eventId);
            command.Parameters.AddWithValue(approvalId);
            command.Parameters.AddWithValue(eventPayload);
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return approvalId;
    }

    private static async Task<JsonElement> ReadTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var taskId = ToolArgumentValidator.RequireGuid(arguments, "taskId");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, type, title, description, priority, status, input::text, constraints::text, updated_at
            from tasks
            where id = $1
            """;
        command.Parameters.AddWithValue(taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ToolGatewayException("task_not_found", "The task was not found in this organization.", 404);
        }

        return JsonSerializer.SerializeToElement(new
        {
            id = reader.GetGuid(0),
            type = reader.GetString(1),
            title = reader.GetString(2),
            description = reader.GetString(3),
            priority = reader.GetString(4),
            status = reader.GetString(5),
            input = CanonicalJson.ParseNormalized(reader.GetString(6)),
            constraints = CanonicalJson.ParseNormalized(reader.GetString(7)),
            updatedAt = reader.GetFieldValue<DateTimeOffset>(8),
        });
    }

    private static async Task<JsonElement> ReadAnalysisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var analysisId = ToolArgumentValidator.RequireGuid(arguments, "analysisId");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, title, status, source_language, requires_human_review, updated_at
            from analyses
            where id = $1
            """;
        command.Parameters.AddWithValue(analysisId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ToolGatewayException("analysis_not_found", "The analysis was not found in this organization.", 404);
        }

        return JsonSerializer.SerializeToElement(new
        {
            id = reader.GetGuid(0),
            title = reader.IsDBNull(1) ? null : reader.GetString(1),
            status = reader.GetString(2),
            sourceLanguage = reader.GetString(3),
            requiresHumanReview = reader.GetBoolean(4),
            updatedAt = reader.GetFieldValue<DateTimeOffset>(5),
        });
    }

    private static async Task<JsonElement> ReadMetricsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select
                (select count(*) from analyses),
                (select count(*) from tasks where status not in ('completed', 'cancelled')),
                (select count(*) from approvals where status = 'pending')
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            analysisCount = reader.GetInt64(0),
            openTaskCount = reader.GetInt64(1),
            pendingApprovalCount = reader.GetInt64(2),
            generatedFrom = "authoritativeDatabase",
        });
    }

    private static async Task<JsonElement> ReadArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var artifactId = ToolArgumentValidator.RequireGuid(arguments, "artifactId");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, artifact_type, title, content_type, inline_content::text, sha256, sensitivity, created_at
            from artifacts
            where id = $1
            """;
        command.Parameters.AddWithValue(artifactId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ToolGatewayException("artifact_not_found", "The artifact was not found in this organization.", 404);
        }

        return JsonSerializer.SerializeToElement(new
        {
            id = reader.GetGuid(0),
            artifactType = reader.GetString(1),
            title = reader.GetString(2),
            contentType = reader.GetString(3),
            content = reader.IsDBNull(4) ? (JsonElement?)null : CanonicalJson.ParseNormalized(reader.GetString(4)),
            sha256 = reader.GetString(5),
            sensitivity = reader.GetString(6),
            createdAt = reader.GetFieldValue<DateTimeOffset>(7),
        });
    }

    private static async Task<JsonElement> CreateTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolGatewayRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var title = ToolArgumentValidator.RequireString(request.Arguments, "title", 200);
        var description = ToolArgumentValidator.RequireOptionalString(request.Arguments, "description", 10_000) ?? string.Empty;
        var type = GetOptionalString(request.Arguments, "type", "general", 100);
        var priority = GetOptionalString(request.Arguments, "priority", "normal", 50);
        var assignedAgentKey = GetOptionalNullableString(request.Arguments, "assignedAgentKey", 100);
        var input = request.Arguments.TryGetProperty("input", out var inputValue)
            ? CanonicalJson.Normalize(inputValue)
            : "{}";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into tasks (
                id, organization_id, type, title, description, priority, status,
                assigned_agent_key, input, constraints, created_by_type, created_by_id,
                created_at, updated_at, version, idempotency_key
            )
            values (
                $1, $2, $3, $4, $5, $6, 'queued',
                $7, $8::jsonb, '{}'::jsonb, 'agent_run', $9,
                $10, $10, 1, $11
            )
            """;
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(request.OrganizationId);
        command.Parameters.AddWithValue(type);
        command.Parameters.AddWithValue(title);
        command.Parameters.AddWithValue(description);
        command.Parameters.AddWithValue(priority);
        command.Parameters.AddWithValue((object?)assignedAgentKey ?? DBNull.Value);
        command.Parameters.AddWithValue(input);
        command.Parameters.AddWithValue(request.AgentRunId.ToString());
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue($"tool-call:{request.IdempotencyKey}");
        await command.ExecuteNonQueryAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(new { taskId = id, status = "queued" });
    }

    private static Task<JsonElement> CreateNoteArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolGatewayRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var taskId = ToolArgumentValidator.RequireGuid(request.Arguments, "taskId");
        var note = ToolArgumentValidator.RequireString(request.Arguments, "note", 20_000);
        return InsertArtifactAsync(
            connection,
            transaction,
            request,
            "task_note",
            $"Task note for {taskId}",
            JsonSerializer.SerializeToElement(new { taskId, note }),
            now,
            cancellationToken);
    }

    private static Task<JsonElement> CreateDraftArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolGatewayRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken) => InsertArtifactAsync(
            connection,
            transaction,
            request,
            GetOptionalString(request.Arguments, "artifactType", "draft", 100),
            ToolArgumentValidator.RequireString(request.Arguments, "title", 200),
            request.Arguments.GetProperty("content"),
            now,
            cancellationToken);

    private static Task<JsonElement> CreateFindingArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolGatewayRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken) => InsertArtifactAsync(
            connection,
            transaction,
            request,
            "agent_finding",
            "Agent finding",
            request.Arguments,
            now,
            cancellationToken);

    private static async Task<JsonElement> InsertArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolGatewayRequest request,
        string artifactType,
        string title,
        JsonElement content,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var normalized = CanonicalJson.Normalize(content);
        var sha256 = CanonicalJson.HashNormalized(normalized);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into artifacts (
                id, organization_id, artifact_type, title, content_type, inline_content,
                sha256, sensitivity, created_by_type, created_by_id, created_at
            )
            values ($1, $2, $3, $4, 'application/json', $5::jsonb, $6, 'internal', 'agent_run', $7, $8)
            """;
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(request.OrganizationId);
        command.Parameters.AddWithValue(artifactType);
        command.Parameters.AddWithValue(title);
        command.Parameters.AddWithValue(normalized);
        command.Parameters.AddWithValue(sha256);
        command.Parameters.AddWithValue(request.AgentRunId.ToString());
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(new { artifactId = id, sha256, status = "draft" });
    }

    private static async Task CompleteToolCallAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid toolCallId,
        string decision,
        string reasonCode,
        string executionStatus,
        JsonElement? output,
        Guid? approvalId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            update tool_calls
            set status = $2,
                decision = $2,
                reason_code = $3,
                execution_status = $4,
                output = $5::jsonb,
                approval_id = $6,
                executed_at = case when $4 = 'completed' then $7 else executed_at end,
                completed_at = case when $4 in ('completed', 'disabled') or $2 in ('denied', 'invalid') then $7 else completed_at end
            where id = $1
            """;
        command.Parameters.AddWithValue(toolCallId);
        command.Parameters.AddWithValue(decision);
        command.Parameters.AddWithValue(reasonCode);
        command.Parameters.AddWithValue(executionStatus);
        command.Parameters.AddWithValue(output is null ? DBNull.Value : CanonicalJson.Normalize(output.Value));
        command.Parameters.AddWithValue((object?)approvalId ?? DBNull.Value);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertToolCallAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid toolCallId,
        ToolGatewayRequest request,
        Guid toolDefinitionId,
        Guid policyVersionId,
        string normalizedInput,
        string inputHash,
        PolicyEvaluation evaluation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into tool_calls (
                id, request_id, organization_id, agent_run_id, task_id, tool_definition_id,
                status, normalized_input, input_hash, idempotency_key, policy_version_id,
                requested_at, decision, reason_code, execution_status, correlation_id
            )
            values (
                $1, $2, $3, $4, $5, $6,
                'requested', $7::jsonb, $8, $9, $10,
                $11, $12, $13, 'notStarted', $14
            )
            """;
        command.Parameters.AddWithValue(toolCallId);
        command.Parameters.AddWithValue(request.RequestId);
        command.Parameters.AddWithValue(request.OrganizationId);
        command.Parameters.AddWithValue(request.AgentRunId);
        command.Parameters.AddWithValue(request.TaskId);
        command.Parameters.AddWithValue(toolDefinitionId);
        command.Parameters.AddWithValue(normalizedInput);
        command.Parameters.AddWithValue(inputHash);
        command.Parameters.AddWithValue(request.IdempotencyKey);
        command.Parameters.AddWithValue(policyVersionId);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(evaluation.Decision);
        command.Parameters.AddWithValue(evaluation.ReasonCode);
        command.Parameters.AddWithValue(request.CorrelationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ToolDefinitionSnapshot> LoadToolDefinitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string toolKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, tool_key, risk_level, side_effect_class, enabled, approval_mode
            from tool_definitions
            where tool_key = $1
            """;
        command.Parameters.AddWithValue(toolKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ToolDefinitionSnapshot(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetString(5))
            : throw new ToolGatewayException("unknown_tool", "The requested tool is not registered.", 404);
    }

    private static async Task<AgentContext> LoadAgentContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolGatewayRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select definition.agent_key, version.tool_permissions::text, task.type, task.created_by_type = 'user'
            from agent_runs run
            join agent_versions version on version.id = run.agent_version_id
            join agent_definitions definition on definition.id = version.agent_definition_id
            join tasks task on task.id = run.task_id
            where run.id = $1
              and run.task_id = $2
              and task.organization_id = $3
              and definition.agent_key = $4
              and definition.active_version_id = version.id
              and definition.status = 'active'
            """;
        command.Parameters.AddWithValue(request.AgentRunId);
        command.Parameters.AddWithValue(request.TaskId);
        command.Parameters.AddWithValue(request.OrganizationId);
        command.Parameters.AddWithValue(request.AgentKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AgentContext(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3))
            : throw new ToolGatewayException(
                "invalid_agent_context",
                "The active agent run, task, organization, and agent key do not form an authorized context.",
                403);
    }

    private static void EnsurePermission(string permissionsJson, string toolKey)
    {
        using var document = JsonDocument.Parse(permissionsJson);
        var permitted = document.RootElement.ValueKind == JsonValueKind.Array &&
            document.RootElement.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                string.Equals(item.GetString(), toolKey, StringComparison.Ordinal));
        if (!permitted)
        {
            throw new ToolGatewayException(
                "tool_permission_denied",
                "The active agent version is not permitted to request this tool.",
                403);
        }
    }

    private static async Task<PolicyContext> LoadPolicyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select version.id, policy.policy_key, version.version_number
            from policies policy
            join policy_versions version on version.id = policy.active_version_id
            where policy.policy_key = 'f0-draft-only'
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PolicyContext(reader.GetGuid(0), $"{reader.GetString(1)}-v{reader.GetInt32(2)}")
            : throw new InvalidOperationException("The active F0 policy version is missing.");
    }

    private static async Task<IReadOnlyDictionary<string, bool>> LoadControlsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var controls = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select control_key, enabled from system_controls";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            controls[reader.GetString(0)] = reader.GetBoolean(1);
        }

        return controls;
    }

    private static async Task<ExistingToolCall?> LoadExistingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        Guid toolDefinitionId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, input_hash, approval_id, coalesce(execution_status, 'notStarted'), output::text
            from tool_calls
            where organization_id = $1
              and tool_definition_id = $2
              and idempotency_key = $3
            """;
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(toolDefinitionId);
        command.Parameters.AddWithValue(idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExistingToolCall(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : CanonicalJson.ParseNormalized(reader.GetString(4)));
    }

    private async Task AuditDecisionAsync(
        ToolGatewayRequest request,
        ToolGatewayResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.AppendAsync(new AuditEventWrite(
            "agent_run",
            request.AgentRunId.ToString(),
            "tool_gateway.decision",
            "tool_call",
            result.ToolCallId.ToString(),
            request.OrganizationId,
            request.RequestId.ToString(),
            request.CorrelationId,
            $"Tool {request.ToolKey} received decision {result.Decision}.",
            JsonSerializer.Serialize(new
            {
                request.ToolKey,
                result.Decision,
                result.ReasonCode,
                result.InputHash,
                result.ApprovalId,
                result.ExecutionStatus,
            }),
            now), cancellationToken);
    }

    internal static async Task SetTenantContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? organizationId,
        bool platformAccess,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select set_config('app.organization_id', $1, true),
                   set_config('app.platform_access', $2, true)
            """;
        command.Parameters.AddWithValue(organizationId?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue(platformAccess ? "true" : "false");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? FindPresentationValue(JsonElement payload, params string[] propertyNames)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static JsonElement FindPresentationElement(JsonElement payload, string propertyName) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(propertyName, out var value)
            ? value.Clone()
            : JsonSerializer.SerializeToElement(Array.Empty<object>());

    private static string GetOptionalString(
        JsonElement arguments,
        string propertyName,
        string fallback,
        int maxLength) => GetOptionalNullableString(arguments, propertyName, maxLength) ?? fallback;

    private static string? GetOptionalNullableString(
        JsonElement arguments,
        string propertyName,
        int maxLength)
    {
        if (!arguments.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) ||
            value.GetString()!.Length > maxLength)
        {
            throw new ToolGatewayException(
                "invalid_tool_arguments",
                $"{propertyName} must be a non-empty string of at most {maxLength} characters.",
                400);
        }

        return value.GetString();
    }

    private static void ValidateRequest(ToolGatewayRequest request)
    {
        if (request.RequestId == Guid.Empty ||
            request.TaskId == Guid.Empty ||
            request.AgentRunId == Guid.Empty ||
            request.OrganizationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.AgentKey) ||
            request.AgentKey.Length > 100 ||
            string.IsNullOrWhiteSpace(request.ToolKey) ||
            request.ToolKey.Length > 200 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 500 ||
            string.IsNullOrWhiteSpace(request.CorrelationId) ||
            request.CorrelationId.Length > 200)
        {
            throw new ToolGatewayException(
                "invalid_tool_request",
                "The tool request contains an invalid identifier or bounded string field.",
                400);
        }
    }

    private sealed record AgentContext(
        string AgentKey,
        string ToolPermissionsJson,
        string TaskType,
        bool OwnerCreatedTask);
    private sealed record PolicyContext(Guid VersionId, string DisplayVersion);
    private sealed record ExistingToolCall(
        Guid ToolCallId,
        string InputHash,
        Guid? ApprovalId,
        string ExecutionStatus,
        JsonElement? Output);
    private sealed record ToolExecution(JsonElement Output, Guid? ApprovalId);
}
