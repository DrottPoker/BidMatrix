using BidMatrix.Application.Audit;
using BidMatrix.Database.Schema;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BidMatrix.Infrastructure.Audit;

public sealed class PostgresAuditWriter(
    [FromKeyedServices(DatabaseServiceCollectionExtensions.AuditDataSourceKey)] NpgsqlDataSource dataSource)
    : IAuditWriter
{
    public async Task<Guid> AppendAsync(AuditEventWrite auditEvent, CancellationToken cancellationToken = default)
    {
        var eventId = Guid.CreateVersion7();
        await using var command = dataSource.CreateCommand("""
            select id
            from append_audit_event(
                $1,
                $2,
                $3,
                $4,
                $5,
                $6,
                $7,
                $8,
                $9,
                $10,
                $11::jsonb,
                $12
            )
            """);
        command.Parameters.AddWithValue(eventId);
        command.Parameters.AddWithValue(auditEvent.ActorType);
        command.Parameters.AddWithValue(auditEvent.ActorId);
        command.Parameters.AddWithValue(auditEvent.Action);
        command.Parameters.AddWithValue((object?)auditEvent.TargetType ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)auditEvent.TargetId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)auditEvent.OrganizationId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)auditEvent.RequestId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)auditEvent.TraceId ?? DBNull.Value);
        command.Parameters.AddWithValue(auditEvent.Summary);
        command.Parameters.AddWithValue(auditEvent.MetadataJson);
        command.Parameters.AddWithValue(auditEvent.CreatedAt);

        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Audit append did not return an event identifier."));
    }
}
