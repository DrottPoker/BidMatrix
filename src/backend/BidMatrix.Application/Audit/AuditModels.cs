namespace BidMatrix.Application.Audit;

public sealed record AuditEventWrite(
    string ActorType,
    string ActorId,
    string Action,
    string? TargetType,
    string? TargetId,
    Guid? OrganizationId,
    string? RequestId,
    string? TraceId,
    string Summary,
    string MetadataJson,
    DateTimeOffset CreatedAt);

public interface IAuditWriter
{
    Task<Guid> AppendAsync(AuditEventWrite auditEvent, CancellationToken cancellationToken = default);
}
