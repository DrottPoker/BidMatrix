using BidMatrix.Application.Identity;
using Npgsql;

namespace BidMatrix.Infrastructure.Identity;

internal sealed class PostgresUserSessionService(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider,
    IdentitySecurityOptions options) : IUserSessionService
{
    private const int MaximumSessionCount = 100;

    public async Task<CreatedUserSession> CreateAsync(
        Guid userId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.CreateVersion7();
        var token = IdentityTokenUtility.Generate();
        _ = IdentityTokenUtility.TryHash(token, out var tokenHash);
        var createdAt = timeProvider.GetUtcNow();
        var absoluteExpiresAt = createdAt.Add(options.SessionAbsoluteLifetime);

        await using var command = dataSource.CreateCommand(
            "select * from create_user_session($1, $2, $3, $4, $5, $6)");
        command.Parameters.AddWithValue(sessionId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(tokenHash);
        command.Parameters.AddWithValue(createdAt);
        command.Parameters.AddWithValue(absoluteExpiresAt);
        command.Parameters.AddWithValue(traceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken) || reader.GetString(0) != "created")
        {
            throw new InvalidOperationException("User session could not be created.");
        }

        return new CreatedUserSession(
            reader.GetGuid(1),
            token,
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<UserSessionValidation> ValidateAsync(
        Guid userId,
        string token,
        Guid securityStamp,
        CancellationToken cancellationToken = default)
    {
        if (!IdentityTokenUtility.TryHash(token, out var tokenHash))
        {
            return new UserSessionValidation(false, null);
        }

        await using var command = dataSource.CreateCommand(
            "select * from validate_user_session($1, $2, $3, $4, $5)");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(tokenHash);
        command.Parameters.AddWithValue(securityStamp);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        command.Parameters.AddWithValue(options.SessionIdleTimeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return new UserSessionValidation(false, null);
        }

        return new UserSessionValidation(
            reader.GetString(0) == "valid",
            reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    public async Task<IReadOnlyList<UserSessionRecord>> ListAsync(
        Guid userId,
        string currentToken,
        CancellationToken cancellationToken = default)
    {
        if (!IdentityTokenUtility.TryHash(currentToken, out var currentTokenHash))
        {
            throw new AccountSecurityException("invalid_session", "The current session is invalid.", 401);
        }

        await using var command = dataSource.CreateCommand(
            "select * from list_user_sessions($1, $2, $3, $4, $5)");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(currentTokenHash);
        command.Parameters.AddWithValue(MaximumSessionCount);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        command.Parameters.AddWithValue(options.SessionIdleTimeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sessions = new List<UserSessionRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new UserSessionRecord(
                reader.GetGuid(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetInt32(8)));
        }

        return sessions;
    }

    public async Task<bool> RevokeAsync(
        Guid sessionId,
        Guid userId,
        string reason,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "select * from revoke_user_session($1, $2, $3, $4, $5)");
        command.Parameters.AddWithValue(sessionId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        command.Parameters.AddWithValue(reason);
        command.Parameters.AddWithValue(traceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) && reader.GetString(0) is "revoked" or "already_revoked";
    }

    public async Task<int> RevokeOthersAsync(
        Guid userId,
        string currentToken,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        if (!IdentityTokenUtility.TryHash(currentToken, out var currentTokenHash))
        {
            throw new AccountSecurityException("invalid_session", "The current session is invalid.", 401);
        }

        await using var command = dataSource.CreateCommand(
            "select revoke_other_user_sessions($1, $2, $3, $4)");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(currentTokenHash);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        command.Parameters.AddWithValue(traceId);

        return (int)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Other session revocation returned no result."));
    }
}
