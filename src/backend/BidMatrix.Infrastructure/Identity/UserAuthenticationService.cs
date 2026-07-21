using System.Text.Json;
using BidMatrix.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BidMatrix.Infrastructure.Identity;

internal sealed class UserAuthenticationService(
    NpgsqlDataSource dataSource,
    IPasswordHasher<AuthenticationPasswordSubject> passwordHasher,
    TimeProvider timeProvider,
    ILogger<UserAuthenticationService> logger) : IUserAuthenticationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthenticationAttempt> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var storedIdentity = await FindIdentityAsync(normalizedEmail, cancellationToken);

        if (storedIdentity is null)
        {
            return AuthenticationAttempt.Failure(AuthenticationStatus.InvalidCredentials);
        }

        if (!string.Equals(storedIdentity.Status, "active", StringComparison.Ordinal))
        {
            return AuthenticationAttempt.Failure(AuthenticationStatus.Disabled);
        }

        if (storedIdentity.LockoutEnd is { } lockoutEnd && lockoutEnd > timeProvider.GetUtcNow())
        {
            return AuthenticationAttempt.Failure(AuthenticationStatus.LockedOut);
        }

        var subject = new AuthenticationPasswordSubject(storedIdentity.UserId);
        PasswordVerificationResult verificationResult;
        try
        {
            verificationResult = passwordHasher.VerifyHashedPassword(subject, storedIdentity.PasswordHash, password);
        }
        catch (FormatException exception)
        {
            logger.LogError(exception, "Stored password credential is malformed for user {UserId}.", storedIdentity.UserId);
            return AuthenticationAttempt.Failure(AuthenticationStatus.InvalidCredentials);
        }

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(storedIdentity.UserId, cancellationToken);
            return AuthenticationAttempt.Failure(AuthenticationStatus.InvalidCredentials);
        }

        var replacementHash = verificationResult == PasswordVerificationResult.SuccessRehashNeeded
            ? passwordHasher.HashPassword(subject, password)
            : null;
        await RecordSuccessAsync(storedIdentity.UserId, replacementHash, cancellationToken);

        return AuthenticationAttempt.Success(new AuthenticatedUser(
            storedIdentity.UserId,
            storedIdentity.Email,
            storedIdentity.DisplayName,
            storedIdentity.SecurityStamp,
            storedIdentity.Memberships,
            storedIdentity.PlatformRoles));
    }

    private async Task<StoredIdentity?> FindIdentityAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select * from get_login_identity($1)");
        command.Parameters.AddWithValue(normalizedEmail);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var membershipPayload = JsonSerializer.Deserialize<List<MembershipPayload>>(reader.GetString(8), JsonOptions) ?? [];
        var platformRoles = JsonSerializer.Deserialize<List<string>>(reader.GetString(9), JsonOptions) ?? [];

        return new StoredIdentity(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetGuid(7),
            membershipPayload
                .Select(item => new OrganizationMembership(item.OrganizationId, item.Role))
                .ToArray(),
            platformRoles);
    }

    private async Task RecordFailureAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            update user_credentials
            set failed_access_count = failed_access_count + 1,
                lockout_end = case
                    when failed_access_count + 1 >= $2 then $3
                    else lockout_end
                end,
                updated_at = $1,
                version = version + 1
            where user_id = $4
            """);
        var now = timeProvider.GetUtcNow();
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(LockoutThreshold);
        command.Parameters.AddWithValue(now.Add(LockoutDuration));
        command.Parameters.AddWithValue(userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RecordSuccessAsync(
        Guid userId,
        string? replacementHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        await using (var credentialCommand = connection.CreateCommand())
        {
            credentialCommand.Transaction = transaction;
            credentialCommand.CommandText = """
                update user_credentials
                set failed_access_count = 0,
                    lockout_end = null,
                    password_hash = coalesce($1, password_hash),
                    updated_at = $2,
                    version = version + 1
                where user_id = $3
                """;
            credentialCommand.Parameters.AddWithValue((object?)replacementHash ?? DBNull.Value);
            credentialCommand.Parameters.AddWithValue(now);
            credentialCommand.Parameters.AddWithValue(userId);
            await credentialCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = """
                update users
                set last_login_at = $1,
                    updated_at = $1
                where id = $2
                """;
            userCommand.Parameters.AddWithValue(now);
            userCommand.Parameters.AddWithValue(userId);
            await userCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record MembershipPayload(Guid OrganizationId, string Role);

    private sealed record StoredIdentity(
        Guid UserId,
        string Email,
        string? DisplayName,
        string Status,
        string PasswordHash,
        int FailedAccessCount,
        DateTimeOffset? LockoutEnd,
        Guid SecurityStamp,
        IReadOnlyList<OrganizationMembership> Memberships,
        IReadOnlyList<string> PlatformRoles);
}
