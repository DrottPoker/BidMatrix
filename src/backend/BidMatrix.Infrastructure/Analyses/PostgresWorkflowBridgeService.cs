using System.Text.Json;
using BidMatrix.Application.Analyses;
using BidMatrix.Application.Audit;
using BidMatrix.Application.Workflows;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace BidMatrix.Infrastructure.Analyses;

public sealed class PostgresWorkflowBridgeService(
    NpgsqlDataSource dataSource,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IWorkflowBridgeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ClaimedOutboxEvent>> ClaimAsync(
        string workerId,
        string eventType,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 100)
        {
            throw new AnalysisOperationException(
                "invalid_worker_id",
                "Worker ID must contain between 1 and 100 characters.",
                StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 200)
        {
            throw new AnalysisOperationException(
                "invalid_event_type",
                "Event type must contain between 1 and 200 characters.",
                StatusCodes.Status400BadRequest);
        }

        limit = Math.Clamp(limit, 1, 25);
        var leaseExpiresAt = timeProvider.GetUtcNow().AddMinutes(1);
        var events = new List<ClaimedOutboxEvent>();

        await using var command = dataSource.CreateCommand("""
            with claimable as (
                select id
                from outbox_events
                where event_type = $1
                  and processed_at is null
                  and dead_lettered_at is null
                  and available_at <= now()
                  and (lease_expires_at is null or lease_expires_at <= now())
                order by occurred_at, id
                for update skip locked
                limit $2
            )
            update outbox_events event
            set lease_owner = $3,
                lease_expires_at = $4,
                attempt_count = event.attempt_count + 1
            from claimable
            where event.id = claimable.id
            returning
                event.id,
                event.event_type,
                event.aggregate_id,
                event.payload::text,
                event.attempt_count,
                event.lease_expires_at
            """);
        command.Parameters.AddWithValue(eventType);
        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(workerId);
        command.Parameters.AddWithValue(leaseExpiresAt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new ClaimedOutboxEvent(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return events;
    }

    public async Task<bool> AcknowledgeAsync(
        Guid eventId,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            update outbox_events
            set processed_at = now(),
                lease_owner = null,
                lease_expires_at = null,
                last_error = null
            where id = $1
              and lease_owner = $2
              and processed_at is null
            """);
        command.Parameters.AddWithValue(eventId);
        command.Parameters.AddWithValue(workerId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> FailAsync(
        Guid eventId,
        string workerId,
        string error,
        CancellationToken cancellationToken = default)
    {
        var safeError = string.IsNullOrWhiteSpace(error)
            ? "Worker reported an unspecified error."
            : error.Trim()[..Math.Min(error.Trim().Length, 2000)];

        await using var command = dataSource.CreateCommand("""
            update outbox_events
            set lease_owner = null,
                lease_expires_at = null,
                last_error = $3,
                available_at = now() + make_interval(secs => least(attempt_count * 5, 300)),
                dead_lettered_at = case when attempt_count >= 5 then now() else null end
            where id = $1
              and lease_owner = $2
              and processed_at is null
            """);
        command.Parameters.AddWithValue(eventId);
        command.Parameters.AddWithValue(workerId);
        command.Parameters.AddWithValue(safeError);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<AnalysisIntakeState?> GetAnalysisIntakeAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        string? status;
        await using (var analysisCommand = connection.CreateCommand())
        {
            analysisCommand.Transaction = transaction;
            analysisCommand.CommandText = "select status from analyses where id = $1";
            analysisCommand.Parameters.AddWithValue(analysisId);
            status = await analysisCommand.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (status is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var fileStatuses = new List<string>();
        await using (var fileCommand = connection.CreateCommand())
        {
            fileCommand.Transaction = transaction;
            fileCommand.CommandText = "select scan_status from analysis_files where analysis_id = $1 order by created_at";
            fileCommand.Parameters.AddWithValue(analysisId);
            await using var reader = await fileCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                fileStatuses.Add(reader.GetString(0));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new AnalysisIntakeState(analysisId, organizationId, status, fileStatuses);
    }

    public async Task MarkAnalysisProcessingAsync(
        Guid organizationId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var changed = await TransitionAnalysisAsync(
            organizationId,
            analysisId,
            ["queued"],
            "processing",
            cancellationToken);

        if (changed)
        {
            await AppendAuditAsync(
                organizationId,
                analysisId,
                "analysis.processing",
                "Analysis intake workflow marked the analysis as processing.",
                correlationId,
                cancellationToken);
        }
    }

    public async Task<Guid> CreateManualReviewTaskAsync(
        Guid organizationId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var taskId = Guid.CreateVersion7();
        var idempotencyKey = $"analysis-manual-review:{analysisId}";
        var inserted = false;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        await using (var verifyAnalysis = connection.CreateCommand())
        {
            verifyAnalysis.Transaction = transaction;
            verifyAnalysis.CommandText = "select status from analyses where id = $1 for update";
            verifyAnalysis.Parameters.AddWithValue(analysisId);
            var status = await verifyAnalysis.ExecuteScalarAsync(cancellationToken) as string;
            if (status is null)
            {
                throw NotFound(analysisId);
            }

            if (status is not ("processing" or "requires_review"))
            {
                throw InvalidWorkflowState(analysisId, status);
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                insert into tasks (
                    id,
                    organization_id,
                    type,
                    title,
                    description,
                    priority,
                    status,
                    assigned_agent_key,
                    input,
                    constraints,
                    created_by_type,
                    created_by_id,
                    created_at,
                    updated_at,
                    version,
                    idempotency_key
                )
                values (
                    $1,
                    $2,
                    'analysis_manual_review',
                    'Review extracted RFP requirements',
                    'Review every extracted requirement, mandatory flag, confidence value, and page citation before use.',
                    'normal',
                    'waiting_for_input',
                    null,
                    $3::jsonb,
                    $4::jsonb,
                    'service',
                    'analysis-intake-workflow',
                    $5,
                    $5,
                    1,
                    $6
                )
                on conflict do nothing
                returning id
                """;
            insert.Parameters.AddWithValue(taskId);
            insert.Parameters.AddWithValue(organizationId);
            insert.Parameters.AddWithValue(JsonSerializer.Serialize(new { analysisId }, JsonOptions));
            insert.Parameters.AddWithValue(JsonSerializer.Serialize(
                new
                {
                    extractionAllowed = true,
                    requiresHumanReview = true,
                    citationsRequired = true,
                    correctionsDeferredToF2 = true,
                },
                JsonOptions));
            insert.Parameters.AddWithValue(now);
            insert.Parameters.AddWithValue(idempotencyKey);
            if (await insert.ExecuteScalarAsync(cancellationToken) is Guid returnedId)
            {
                taskId = returnedId;
                inserted = true;
            }
        }

        if (!inserted)
        {
            await using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "select id from tasks where idempotency_key = $1";
            existing.Parameters.AddWithValue(idempotencyKey);
            taskId = (Guid)(await existing.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Idempotent manual-review task was not found."));
        }
        else
        {
            await InsertOutboxEventAsync(
                connection,
                transaction,
                "task.created.v1",
                taskId,
                organizationId,
                correlationId,
                new { taskId, analysisId, type = "analysis_manual_review" },
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (inserted)
        {
            await AppendAuditAsync(
                organizationId,
                taskId,
                "task.created",
                "Analysis intake workflow created one manual-review task.",
                correlationId,
                cancellationToken);
        }

        return taskId;
    }

    public async Task MarkAnalysisRequiresReviewAsync(
        Guid organizationId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var changed = false;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update analyses
                set status = 'requires_review',
                    updated_at = $1,
                    version = version + 1
                where id = $2 and status = 'processing'
                """;
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(analysisId);
            changed = await update.ExecuteNonQueryAsync(cancellationToken) == 1;
        }

        if (!changed)
        {
            await using var statusCommand = connection.CreateCommand();
            statusCommand.Transaction = transaction;
            statusCommand.CommandText = "select status from analyses where id = $1";
            statusCommand.Parameters.AddWithValue(analysisId);
            var status = await statusCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (status is null)
            {
                throw NotFound(analysisId);
            }

            if (status != "requires_review")
            {
                throw InvalidWorkflowState(analysisId, status);
            }
        }
        else
        {
            await InsertOutboxEventAsync(
                connection,
                transaction,
                "analysis.requires_review.v1",
                analysisId,
                organizationId,
                correlationId,
                new { analysisId, capabilityStatus = "manualReviewRequired" },
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (changed)
        {
            await AppendAuditAsync(
                organizationId,
                analysisId,
                "analysis.requires_review",
                "F1 extraction completed and requires manual review before requirements can be relied upon.",
                correlationId,
                cancellationToken);
        }
    }

    private async Task<bool> TransitionAnalysisAsync(
        Guid organizationId,
        Guid analysisId,
        IReadOnlyCollection<string> allowedSourceStatuses,
        string targetStatus,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        string? status;
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "select status from analyses where id = $1 for update";
            lockCommand.Parameters.AddWithValue(analysisId);
            status = await lockCommand.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (status is null)
        {
            throw NotFound(analysisId);
        }

        if (status == targetStatus || status == "requires_review")
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        if (!allowedSourceStatuses.Contains(status, StringComparer.Ordinal))
        {
            throw InvalidWorkflowState(analysisId, status);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update analyses
                set status = $1,
                    started_at = coalesce(started_at, $2),
                    updated_at = $2,
                    version = version + 1
                where id = $3
                """;
            update.Parameters.AddWithValue(targetStatus);
            update.Parameters.AddWithValue(timeProvider.GetUtcNow());
            update.Parameters.AddWithValue(analysisId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task AppendAuditAsync(
        Guid organizationId,
        Guid targetId,
        string action,
        string summary,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await auditWriter.AppendAsync(
            new AuditEventWrite(
                "service",
                "analysis-intake-workflow",
                action,
                action.StartsWith("task.", StringComparison.Ordinal) ? "task" : "analysis",
                targetId.ToString(),
                organizationId,
                correlationId,
                null,
                summary,
                "{}",
                timeProvider.GetUtcNow()),
            cancellationToken);
    }

    private static async Task SetTenantContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select set_config('app.organization_id', $1, true)";
        command.Parameters.AddWithValue(organizationId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOutboxEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventType,
        Guid aggregateId,
        Guid organizationId,
        string correlationId,
        object payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.CreateVersion7();
        var envelope = JsonSerializer.Serialize(
            new
            {
                eventId,
                eventType,
                occurredAt,
                correlationId,
                causationId = (string?)null,
                organizationId,
                actor = new { type = "service", id = "analysis-intake-workflow" },
                payload,
            },
            JsonOptions);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into outbox_events (
                id,
                event_type,
                aggregate_type,
                aggregate_id,
                payload,
                occurred_at,
                available_at
            )
            values ($1, $2, $3, $4, $5::jsonb, $6, $6)
            """;
        command.Parameters.AddWithValue(eventId);
        command.Parameters.AddWithValue(eventType);
        command.Parameters.AddWithValue(eventType.StartsWith("task.", StringComparison.Ordinal) ? "task" : "analysis");
        command.Parameters.AddWithValue(aggregateId);
        command.Parameters.AddWithValue(envelope);
        command.Parameters.AddWithValue(occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AnalysisOperationException NotFound(Guid analysisId) => new(
        "analysis_not_found",
        $"Analysis {analysisId} was not found.",
        StatusCodes.Status404NotFound);

    private static AnalysisOperationException InvalidWorkflowState(Guid analysisId, string? status) => new(
        "analysis_workflow_state_invalid",
        $"Analysis {analysisId} cannot perform the intake operation while its status is {status ?? "unknown"}.",
        StatusCodes.Status409Conflict);
}
