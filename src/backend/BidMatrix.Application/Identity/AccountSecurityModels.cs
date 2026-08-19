namespace BidMatrix.Application.Identity;

public sealed record CreatedUserSession(
    Guid Id,
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset AbsoluteExpiresAt);

public sealed record UserSessionRecord(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset AbsoluteExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    string Status,
    bool IsCurrent,
    int Version);

public sealed record UserSessionValidation(bool IsValid, Guid? SessionId);

public interface IUserSessionService
{
    Task<CreatedUserSession> CreateAsync(
        Guid userId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<UserSessionValidation> ValidateAsync(
        Guid userId,
        string token,
        Guid securityStamp,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSessionRecord>> ListAsync(
        Guid userId,
        string currentToken,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        Guid sessionId,
        Guid userId,
        string reason,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<int> RevokeOthersAsync(
        Guid userId,
        string currentToken,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed record PasswordChangeCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword,
    string TraceId);

public sealed record AccountRecoveryRecord(
    Guid Id,
    Guid UserId,
    string Email,
    string? DisplayName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? RevokedAt,
    int Version);

public sealed record CreatedAccountRecovery(
    AccountRecoveryRecord Recovery,
    string Token,
    string RecoveryUrl);

public sealed record CreateAccountRecoveryCommand(
    Guid RequestedByUserId,
    string Email,
    string TraceId);

public sealed record RevokeAccountRecoveryCommand(
    Guid RecoveryId,
    Guid RevokedByUserId,
    string TraceId);

public sealed record AccountRecoveryInspection(
    Guid RecoveryId,
    Guid UserId,
    string Email,
    string Status,
    DateTimeOffset ExpiresAt);

public sealed record ResetAccountPasswordCommand(
    string Token,
    string NewPassword,
    string TraceId);

public interface IAccountSecurityService
{
    Task ChangePasswordAsync(
        PasswordChangeCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountRecoveryRecord>> ListRecoveriesAsync(
        Guid requestedByUserId,
        CancellationToken cancellationToken = default);

    Task<CreatedAccountRecovery> CreateRecoveryAsync(
        CreateAccountRecoveryCommand command,
        CancellationToken cancellationToken = default);

    Task<AccountRecoveryRecord> RevokeRecoveryAsync(
        RevokeAccountRecoveryCommand command,
        CancellationToken cancellationToken = default);

    Task<AccountRecoveryInspection> InspectRecoveryAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(
        ResetAccountPasswordCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class AccountSecurityException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}
