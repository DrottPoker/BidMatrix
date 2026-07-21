using System.Data;
using System.Diagnostics;
using System.Text.Json;
using BidMatrix.Application.Audit;
using BidMatrix.Application.Tools;
using Npgsql;

namespace BidMatrix.Infrastructure.Tools;

public sealed class PostgresApprovalService(
    NpgsqlDataSource dataSource,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IApprovalService
{
    private static readonly HashSet<string> ApprovalStatuses = new(StringComparer.Ordinal)
    {
        "pending",
        "approved",
        "rejected",
        "expired",
        "cancelled",
        "invalidated",
    };

    public async Task<IReadOnlyList<ApprovalRecord>> ListAsync(
        Guid? organizationId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        if (status is not null && !ApprovalStatuses.Contains(status))
        {
            throw new ToolGatewayException("invalid_approval_status", "The approval status filter is invalid.", 400);
        }

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
        command.CommandText = ApprovalSelect + """
            where ($1::uuid is null or approval.organization_id = $1)
              and ($2::text is null or approval.status = $2)
            order by approval.requested_at desc
            limit 250
            """;
        command.Parameters.AddWithValue((object?)organizationId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)status ?? DBNull.Value);
        var records = new List<ApprovalRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(MapRecord(reader));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return records;
    }

    public async Task<ApprovalRecord?> GetAsync(
        Guid approvalId,
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
        var record = await LoadRecordAsync(connection, transaction, approvalId, false, cancellationToken);
        if (record is not null && organizationId is not null && record.OrganizationId != organizationId)
        {
            record = null;
        }

        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<ApprovalRecord> DecideAsync(
        ApprovalDecisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateDecision(command);
        var now = timeProvider.GetUtcNow();
        var normalizedPayload = CanonicalJson.Normalize(command.Payload);
        var payloadHash = CanonicalJson.HashNormalized(normalizedPayload);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(
            connection,
            transaction,
            command.OrganizationId,
            true,
            cancellationToken);

        var current = await LoadStateAsync(
            connection,
            transaction,
            command.ApprovalId,
            true,
            cancellationToken)
            ?? throw new ToolGatewayException("approval_not_found", "The approval was not found.", 404);
        if (current.OrganizationId != command.OrganizationId)
        {
            throw new ToolGatewayException("approval_not_found", "The approval was not found.", 404);
        }

        current = current with { DecidingUserId = command.OwnerUserId };

        if (current.Status == "pending" && current.ExpiresAt <= now)
        {
            await SetFinalStatusAsync(
                connection,
                transaction,
                current,
                "expired",
                "notStarted",
                null,
                now,
                cancellationToken);
            await InsertApprovalDecisionOutboxAsync(
                connection,
                transaction,
                current.Id,
                current.OrganizationId,
                "expired",
                "notStarted",
                command.CorrelationId,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var expired = await GetRequiredAsync(command.ApprovalId, command.OrganizationId, cancellationToken);
            await AuditDecisionAsync(command, expired, "approval.expired", now, cancellationToken);
            return expired;
        }

        if (current.Status != "pending" || current.Version != command.ExpectedVersion)
        {
            await transaction.CommitAsync(cancellationToken);
            return await GetRequiredAsync(command.ApprovalId, command.OrganizationId, cancellationToken);
        }

        if (command.Decision != ApprovalDecisions.EditAndCreateRevision &&
            !string.Equals(payloadHash, current.PayloadHash, StringComparison.Ordinal))
        {
            await SetFinalStatusAsync(
                connection,
                transaction,
                current,
                "invalidated",
                "notStarted",
                "Payload changed before the owner decision.",
                now,
                cancellationToken);
            await InsertApprovalDecisionOutboxAsync(
                connection,
                transaction,
                current.Id,
                current.OrganizationId,
                "invalidated",
                "notStarted",
                command.CorrelationId,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var invalidated = await GetRequiredAsync(command.ApprovalId, command.OrganizationId, cancellationToken);
            await AuditDecisionAsync(command, invalidated, "approval.invalidated", now, cancellationToken);
            return invalidated;
        }

        Guid resultId;
        string auditAction;
        string finalStatus;
        string finalExecutionStatus;
        switch (command.Decision)
        {
            case ApprovalDecisions.Approve:
                await SetFinalStatusAsync(
                    connection,
                    transaction,
                    current,
                    "approved",
                    current.TechnicallyEnabled ? "pending" : "disabled",
                    command.Note,
                    now,
                    cancellationToken);
                resultId = current.Id;
                auditAction = "approval.approved";
                finalStatus = "approved";
                finalExecutionStatus = current.TechnicallyEnabled ? "pending" : "disabled";
                break;
            case ApprovalDecisions.Reject:
                await SetFinalStatusAsync(
                    connection,
                    transaction,
                    current,
                    "rejected",
                    "notStarted",
                    command.Note,
                    now,
                    cancellationToken);
                resultId = current.Id;
                auditAction = "approval.rejected";
                finalStatus = "rejected";
                finalExecutionStatus = "notStarted";
                break;
            case ApprovalDecisions.Cancel:
                await SetFinalStatusAsync(
                    connection,
                    transaction,
                    current,
                    "cancelled",
                    "notStarted",
                    command.Note,
                    now,
                    cancellationToken);
                resultId = current.Id;
                auditAction = "approval.cancelled";
                finalStatus = "cancelled";
                finalExecutionStatus = "notStarted";
                break;
            case ApprovalDecisions.EditAndCreateRevision:
                if (string.Equals(payloadHash, current.PayloadHash, StringComparison.Ordinal))
                {
                    throw new ToolGatewayException(
                        "approval_revision_unchanged",
                        "An edited approval revision must contain a changed payload.",
                        409);
                }

                await SetFinalStatusAsync(
                    connection,
                    transaction,
                    current,
                    "invalidated",
                    "notStarted",
                    "Replaced by an owner-edited revision.",
                    now,
                    cancellationToken);
                resultId = await CreateRevisionAsync(
                    connection,
                    transaction,
                    current,
                    command,
                    normalizedPayload,
                    payloadHash,
                    now,
                    cancellationToken);
                auditAction = "approval.revised";
                finalStatus = "invalidated";
                finalExecutionStatus = "notStarted";
                break;
            default:
                throw new UnreachableException();
        }

        await InsertApprovalDecisionOutboxAsync(
            connection,
            transaction,
            current.Id,
            current.OrganizationId,
            finalStatus,
            finalExecutionStatus,
            command.CorrelationId,
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var result = await GetRequiredAsync(resultId, command.OrganizationId, cancellationToken);
        await AuditDecisionAsync(command, result, auditAction, now, cancellationToken);
        return result;
    }

    public async Task<ApprovalRecord?> ExpireAsync(
        Guid approvalId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await PostgresToolGatewayService.SetTenantContextAsync(connection, transaction, null, true, cancellationToken);
        var current = await LoadStateAsync(connection, transaction, approvalId, true, cancellationToken);
        if (current is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var expired = current.Status == "pending" && current.ExpiresAt <= now;
        if (expired)
        {
            await SetFinalStatusAsync(
                connection,
                transaction,
                current,
                "expired",
                "notStarted",
                null,
                now,
                cancellationToken);
            await InsertApprovalDecisionOutboxAsync(
                connection,
                transaction,
                current.Id,
                current.OrganizationId,
                "expired",
                "notStarted",
                $"approval-expiry-{current.Id}",
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var result = await GetAsync(approvalId, null, cancellationToken);
        if (expired && result is not null)
        {
            await auditWriter.AppendAsync(new AuditEventWrite(
                "system",
                "approval-workflow",
                "approval.expired",
                "approval",
                result.Id.ToString(),
                result.OrganizationId,
                $"approval-expiry-{result.Id}",
                null,
                $"Approval {result.Id} expired.",
                JsonSerializer.Serialize(new { result.Status, result.PayloadHash, result.Version }),
                now), cancellationToken);
        }

        return result;
    }

    private async Task<ApprovalRecord> GetRequiredAsync(
        Guid approvalId,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        await GetAsync(approvalId, organizationId, cancellationToken)
        ?? throw new InvalidOperationException("Approval disappeared after a committed decision.");

    private static async Task<Guid> CreateRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalState current,
        ApprovalDecisionCommand decision,
        string normalizedPayload,
        string payloadHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var expiresAt = now.AddHours(24);
        var presentation = UpdatePresentation(current.PresentationJson, normalizedPayload);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                insert into approvals (
                    id, organization_id, tool_call_id, task_id, action_type, status, summary,
                    normalized_payload, payload_hash, policy_version_id, requested_by_agent_run_id,
                    requested_at, expires_at, execution_status, version, presentation,
                    supersedes_approval_id
                )
                values (
                    $1, $2, $3, $4, $5, 'pending', $6,
                    $7::jsonb, $8, $9, $10,
                    $11, $12, 'notStarted', $13, $14::jsonb,
                    $15
                )
                """;
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(current.OrganizationId);
            command.Parameters.AddWithValue((object?)current.ToolCallId ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)current.TaskId ?? DBNull.Value);
            command.Parameters.AddWithValue(current.ActionType);
            command.Parameters.AddWithValue(current.Summary);
            command.Parameters.AddWithValue(normalizedPayload);
            command.Parameters.AddWithValue(payloadHash);
            command.Parameters.AddWithValue(current.PolicyVersionId);
            command.Parameters.AddWithValue((object?)current.RequestedByAgentRunId ?? DBNull.Value);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(expiresAt);
            command.Parameters.AddWithValue(current.Version + 1);
            command.Parameters.AddWithValue(presentation);
            command.Parameters.AddWithValue(current.Id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (current.ToolCallId is { } toolCallId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                update tool_calls
                set approval_id = $2,
                    normalized_input = $3::jsonb,
                    input_hash = $4,
                    status = 'approvalRequired',
                    decision = 'approvalRequired',
                    reason_code = 'owner_edited_revision',
                    execution_status = 'pending',
                    completed_at = null
                where id = $1
                """;
            update.Parameters.AddWithValue(toolCallId);
            update.Parameters.AddWithValue(id);
            update.Parameters.AddWithValue(normalizedPayload);
            update.Parameters.AddWithValue(payloadHash);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertApprovalOutboxAsync(
            connection,
            transaction,
            id,
            current.OrganizationId,
            decision.CorrelationId,
            expiresAt,
            now,
            cancellationToken);
        return id;
    }

    private static async Task SetFinalStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalState current,
        string status,
        string executionStatus,
        string? decisionNote,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                update approvals
                set status = $2,
                    execution_status = $3,
                    decided_by_user_id = case when $4::uuid is null then decided_by_user_id else $4 end,
                    decided_at = $5,
                    decision_note = $6,
                    version = version + 1
                where id = $1 and status = 'pending'
                """;
            command.Parameters.AddWithValue(current.Id);
            command.Parameters.AddWithValue(status);
            command.Parameters.AddWithValue(executionStatus);
            command.Parameters.AddWithValue((object?)current.DecidingUserId ?? DBNull.Value);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue((object?)decisionNote ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (current.ToolCallId is not { } toolCallId)
        {
            return;
        }

        var toolStatus = status == "approved" ? executionStatus : status;
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            update tool_calls
            set status = $2,
                execution_status = $3,
                reason_code = $4,
                completed_at = case when $3 <> 'pending' then $5 else completed_at end,
                output = case when $3 = 'disabled'
                    then '{"status":"disabled","externalActionOccurred":false}'::jsonb
                    else output
                end
            where id = $1
            """;
        update.Parameters.AddWithValue(toolCallId);
        update.Parameters.AddWithValue(toolStatus);
        update.Parameters.AddWithValue(executionStatus);
        update.Parameters.AddWithValue($"approval_{status}");
        update.Parameters.AddWithValue(now);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertApprovalOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid approvalId,
        Guid organizationId,
        string correlationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            organizationId,
            correlationId,
            payload = new
            {
                approvalId,
                workflowId = $"approval-{approvalId}",
                expiresAt,
            },
        });
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into outbox_events (
                id, event_type, aggregate_type, aggregate_id, payload,
                occurred_at, available_at, attempt_count
            )
            values ($1, 'approval.requested.v1', 'approval', $2, $3::jsonb, $4, $4, 0)
            """;
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(approvalId);
        command.Parameters.AddWithValue(payload);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertApprovalDecisionOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid approvalId,
        Guid organizationId,
        string status,
        string executionStatus,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            organizationId,
            correlationId,
            payload = new
            {
                approvalId,
                workflowId = $"approval-{approvalId}",
                status,
                executionStatus,
            },
        });
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into outbox_events (
                id, event_type, aggregate_type, aggregate_id, payload,
                occurred_at, available_at, attempt_count
            )
            values ($1, 'approval.decided.v1', 'approval', $2, $3::jsonb, $4, $4, 0)
            """;
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(approvalId);
        command.Parameters.AddWithValue(payload);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ApprovalRecord?> LoadRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid approvalId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ApprovalSelect + " where approval.id = $1" + (forUpdate ? " for update" : string.Empty);
        command.Parameters.AddWithValue(approvalId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRecord(reader) : null;
    }

    private static async Task<ApprovalState?> LoadStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid approvalId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select
                approval.id,
                approval.organization_id,
                approval.tool_call_id,
                approval.task_id,
                approval.action_type,
                approval.status,
                approval.summary,
                approval.payload_hash,
                approval.policy_version_id,
                approval.requested_by_agent_run_id,
                approval.expires_at,
                approval.version,
                approval.presentation::text,
                coalesce((approval.presentation->>'technicallyEnabled')::boolean, false)
            from approvals approval
            where approval.id = $1
            """ + (forUpdate ? " for update" : string.Empty);
        command.Parameters.AddWithValue(approvalId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ApprovalState(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetInt32(11),
            reader.GetString(12),
            reader.GetBoolean(13),
            null);
    }

    private static ApprovalRecord MapRecord(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.IsDBNull(2) ? null : reader.GetGuid(2),
        reader.IsDBNull(3) ? null : reader.GetGuid(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        CanonicalJson.ParseNormalized(reader.GetString(7)),
        reader.GetString(8),
        $"{reader.GetString(9)}-v{reader.GetInt32(10)}",
        reader.GetString(11),
        reader.GetBoolean(12),
        reader.GetFieldValue<DateTimeOffset>(13),
        reader.GetFieldValue<DateTimeOffset>(14),
        reader.IsDBNull(15) ? null : reader.GetGuid(15),
        reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
        reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetString(18),
        reader.GetInt32(19),
        reader.IsDBNull(20) ? null : reader.GetGuid(20));

    private async Task AuditDecisionAsync(
        ApprovalDecisionCommand command,
        ApprovalRecord result,
        string action,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.AppendAsync(new AuditEventWrite(
            "user",
            command.OwnerUserId.ToString(),
            action,
            "approval",
            result.Id.ToString(),
            result.OrganizationId,
            command.CorrelationId,
            command.CorrelationId,
            $"Approval {result.Id} is {result.Status}.",
            JsonSerializer.Serialize(new
            {
                result.Status,
                result.PayloadHash,
                result.ExecutionStatus,
                result.Version,
                result.SupersedesApprovalId,
            }),
            now), cancellationToken);
    }

    private static string UpdatePresentation(string presentationJson, string normalizedPayload)
    {
        using var presentation = JsonDocument.Parse(presentationJson);
        var values = presentation.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        values["ownerEdited"] = JsonSerializer.SerializeToElement(true);
        values["revisedPayloadHash"] = JsonSerializer.SerializeToElement(CanonicalJson.HashNormalized(normalizedPayload));
        return JsonSerializer.Serialize(values);
    }

    private static void ValidateDecision(ApprovalDecisionCommand command)
    {
        if (command.ApprovalId == Guid.Empty ||
            command.OrganizationId == Guid.Empty ||
            command.OwnerUserId == Guid.Empty ||
            command.ExpectedVersion < 1 ||
            command.Payload.ValueKind == JsonValueKind.Undefined ||
            string.IsNullOrWhiteSpace(command.CorrelationId) ||
            command.CorrelationId.Length > 200 ||
            command.Note?.Length > 2_000 ||
            command.Decision is not (
                ApprovalDecisions.Approve or
                ApprovalDecisions.Reject or
                ApprovalDecisions.EditAndCreateRevision or
                ApprovalDecisions.Cancel))
        {
            throw new ToolGatewayException("invalid_approval_decision", "The approval decision is invalid.", 400);
        }
    }

    private const string ApprovalSelect = """
        select
            approval.id,
            approval.organization_id,
            approval.tool_call_id,
            approval.task_id,
            approval.action_type,
            approval.status,
            approval.summary,
            approval.normalized_payload::text,
            approval.payload_hash,
            policy.policy_key,
            version.version_number,
            coalesce(approval.presentation->>'riskLevel', 'unknown'),
            coalesce((approval.presentation->>'technicallyEnabled')::boolean, false),
            approval.requested_at,
            approval.expires_at,
            approval.decided_by_user_id,
            approval.decided_at,
            approval.decision_note,
            approval.execution_status,
            approval.version,
            approval.supersedes_approval_id
        from approvals approval
        join policy_versions version on version.id = approval.policy_version_id
        join policies policy on policy.id = version.policy_id
        """;

    private sealed record ApprovalState(
        Guid Id,
        Guid OrganizationId,
        Guid? ToolCallId,
        Guid? TaskId,
        string ActionType,
        string Status,
        string Summary,
        string PayloadHash,
        Guid PolicyVersionId,
        Guid? RequestedByAgentRunId,
        DateTimeOffset ExpiresAt,
        int Version,
        string PresentationJson,
        bool TechnicallyEnabled,
        Guid? DecidingUserId);
}
