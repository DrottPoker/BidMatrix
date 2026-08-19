namespace BidMatrix.Application.Identity;

public sealed record FederatedIdentityProfile(
    string Issuer,
    string SubjectHash,
    string Email,
    DateTimeOffset AuthenticatedAt,
    string? DisplayName = null);

public sealed record FederatedIdentityRecord(
    Guid Id,
    string ProviderName,
    string Issuer,
    string EmailAtLink,
    string Status,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastAuthenticatedAt,
    DateTimeOffset? RevokedAt,
    int Version);

public sealed record AuthenticatedFederatedIdentity(
    Guid IdentityId,
    AuthenticatedUser User);

public sealed record LinkFederatedIdentityCommand(
    Guid UserId,
    Guid SessionId,
    Guid SecurityStamp,
    string ProviderName,
    FederatedIdentityProfile Profile,
    string TraceId);

public sealed record RegisterFederatedAccountCommand(
    string ProviderName,
    FederatedIdentityProfile Profile,
    string TraceId);

public sealed record RevokeFederatedIdentityCommand(
    Guid IdentityId,
    Guid UserId,
    bool NativeLoginEnabled,
    string TraceId);

public sealed record RevokedFederatedIdentity(
    FederatedIdentityRecord Identity,
    int RevokedSessionCount);

public interface IFederatedIdentityService
{
    Task<AuthenticatedFederatedIdentity> AuthenticateAsync(
        FederatedIdentityProfile profile,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedFederatedIdentity> LinkAsync(
        LinkFederatedIdentityCommand command,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedFederatedIdentity> RegisterAsync(
        RegisterFederatedAccountCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FederatedIdentityRecord>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> HasNativePasswordAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<RevokedFederatedIdentity> RevokeAsync(
        RevokeFederatedIdentityCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateAsync(
        Guid identityId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class FederatedIdentityException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}
