using System.ComponentModel.DataAnnotations;
using BidMatrix.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BidMatrix.Infrastructure.Identity;

internal sealed class PostgresAccountSecurityService(
    NpgsqlDataSource dataSource,
    IPasswordHasher<AuthenticationPasswordSubject> passwordHasher,
    TimeProvider timeProvider,
    IdentitySecurityOptions options,
    ILogger<PostgresAccountSecurityService> logger) : IAccountSecurityService
{
    private const int MaximumRecoveryCount = 100;
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 128;

    public async Task ChangePasswordAsync(
        PasswordChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCurrentPassword(command.CurrentPassword);
        var newPassword = ValidateNewPassword(command.NewPassword);
        var identity = await GetPasswordIdentityAsync(command.UserId, cancellationToken);
        if (identity is null || identity.Status != "active")
        {
            throw Conflict("account_unavailable", "The account is unavailable.");
        }

        var subject = new AuthenticationPasswordSubject(command.UserId);
        PasswordVerificationResult currentVerification;
        PasswordVerificationResult replacementVerification;
        try
        {
            currentVerification = passwordHasher.VerifyHashedPassword(
                subject,
                identity.PasswordHash,
                command.CurrentPassword);
            replacementVerification = passwordHasher.VerifyHashedPassword(
                subject,
                identity.PasswordHash,
                newPassword);
        }
        catch (FormatException exception)
        {
            logger.LogError(exception, "Stored password credential is malformed for user {UserId}.", command.UserId);
            throw Conflict("account_unavailable", "The account is unavailable.");
        }

        if (currentVerification == PasswordVerificationResult.Failed)
        {
            throw Validation("current_password_invalid", "The current password is invalid.");
        }

        if (replacementVerification != PasswordVerificationResult.Failed)
        {
            throw Validation("new_password_unchanged", "The new password must differ from the current password.");
        }

        var changedAt = timeProvider.GetUtcNow();
        var passwordHash = passwordHasher.HashPassword(subject, newPassword);
        var securityStamp = Guid.CreateVersion7();
        await using var databaseCommand = dataSource.CreateCommand(
            "select * from change_user_password($1, $2, $3, $4, $5, $6)");
        databaseCommand.Parameters.AddWithValue(command.UserId);
        databaseCommand.Parameters.AddWithValue(identity.SecurityStamp);
        databaseCommand.Parameters.AddWithValue(passwordHash);
        databaseCommand.Parameters.AddWithValue(securityStamp);
        databaseCommand.Parameters.AddWithValue(changedAt);
        databaseCommand.Parameters.AddWithValue(command.TraceId);
        await using var reader = await databaseCommand.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Password change returned no result.");
        }

        var resultStatus = reader.GetString(0);
        if (resultStatus != "changed")
        {
            throw resultStatus switch
            {
                "conflict" => Conflict(
                    "credential_changed",
                    "The credential changed during the request. Sign in and try again."),
                "unavailable" => Conflict("account_unavailable", "The account is unavailable."),
                _ => new InvalidOperationException($"Unexpected password change result '{resultStatus}'."),
            };
        }
    }

    public async Task<IReadOnlyList<AccountRecoveryRecord>> ListRecoveriesAsync(
        Guid requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "select * from list_account_recovery_tokens($1, $2, $3)");
        command.Parameters.AddWithValue(requestedByUserId);
        command.Parameters.AddWithValue(MaximumRecoveryCount);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var recoveries = new List<AccountRecoveryRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            recoveries.Add(new AccountRecoveryRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetInt32(9)));
        }

        return recoveries;
    }

    public async Task<CreatedAccountRecovery> CreateRecoveryAsync(
        CreateAccountRecoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        var email = ValidateEmail(command.Email);
        var recoveryId = Guid.CreateVersion7();
        var token = IdentityTokenUtility.Generate();
        _ = IdentityTokenUtility.TryHash(token, out var tokenHash);
        var createdAt = timeProvider.GetUtcNow();
        var expiresAt = createdAt.Add(options.RecoveryLifetime);

        await using var databaseCommand = dataSource.CreateCommand(
            "select * from create_account_recovery_token($1, $2, $3, $4, $5, $6, $7)");
        databaseCommand.Parameters.AddWithValue(recoveryId);
        databaseCommand.Parameters.AddWithValue(email.ToUpperInvariant());
        databaseCommand.Parameters.AddWithValue(tokenHash);
        databaseCommand.Parameters.AddWithValue(command.RequestedByUserId);
        databaseCommand.Parameters.AddWithValue(createdAt);
        databaseCommand.Parameters.AddWithValue(expiresAt);
        databaseCommand.Parameters.AddWithValue(command.TraceId);
        await using var reader = await databaseCommand.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Account recovery creation returned no result.");
        }

        var resultStatus = reader.GetString(0);
        if (resultStatus != "created")
        {
            throw resultStatus switch
            {
                "not_found" => new AccountSecurityException(
                    "account_not_found",
                    "No active password account matches that email address.",
                    404),
                "forbidden" => new AccountSecurityException(
                    "platform_owner_required",
                    "Platform owner access is required.",
                    403),
                "invalid_expiry" => new InvalidOperationException("The recovery expiration is invalid."),
                _ => new InvalidOperationException($"Unexpected recovery creation result '{resultStatus}'."),
            };
        }

        var recovery = new AccountRecoveryRecord(
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            null,
            null,
            reader.GetInt32(8));
        var recoveryUrl = new Uri(options.PublicBaseUri, $"/recover#token={token}").AbsoluteUri;
        return new CreatedAccountRecovery(recovery, token, recoveryUrl);
    }

    public async Task<AccountRecoveryRecord> RevokeRecoveryAsync(
        RevokeAccountRecoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var databaseCommand = dataSource.CreateCommand(
            "select * from revoke_account_recovery_token($1, $2, $3, $4)");
        databaseCommand.Parameters.AddWithValue(command.RecoveryId);
        databaseCommand.Parameters.AddWithValue(command.RevokedByUserId);
        databaseCommand.Parameters.AddWithValue(timeProvider.GetUtcNow());
        databaseCommand.Parameters.AddWithValue(command.TraceId);
        await using var reader = await databaseCommand.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Account recovery revocation returned no result.");
        }

        var resultStatus = reader.GetString(0);
        if (resultStatus is "not_found" or "forbidden")
        {
            throw resultStatus == "not_found"
                ? new AccountSecurityException("recovery_not_found", "The recovery link was not found.", 404)
                : new AccountSecurityException(
                    "platform_owner_required",
                    "Platform owner access is required.",
                    403);
        }

        return new AccountRecoveryRecord(
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetInt32(10));
    }

    public async Task<AccountRecoveryInspection> InspectRecoveryAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashRecoveryToken(token);
        await using var command = dataSource.CreateCommand(
            "select * from inspect_account_recovery_token($1, $2)");
        command.Parameters.AddWithValue(tokenHash);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw RecoveryNotFound();
        }

        var status = reader.GetString(0);
        if (status == "invalid")
        {
            throw RecoveryNotFound();
        }

        if (status != "pending")
        {
            throw RecoveryUnavailable();
        }

        return new AccountRecoveryInspection(
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            status,
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task ResetPasswordAsync(
        ResetAccountPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashRecoveryToken(command.Token);
        var newPassword = ValidateNewPassword(command.NewPassword);
        var inspection = await InspectRecoveryAsync(command.Token, cancellationToken);
        var subject = new AuthenticationPasswordSubject(inspection.UserId);
        var passwordHash = passwordHasher.HashPassword(subject, newPassword);
        var resetAt = timeProvider.GetUtcNow();

        await using var databaseCommand = dataSource.CreateCommand(
            "select * from reset_password_with_recovery_token($1, $2, $3, $4, $5)");
        databaseCommand.Parameters.AddWithValue(tokenHash);
        databaseCommand.Parameters.AddWithValue(passwordHash);
        databaseCommand.Parameters.AddWithValue(Guid.CreateVersion7());
        databaseCommand.Parameters.AddWithValue(resetAt);
        databaseCommand.Parameters.AddWithValue(command.TraceId);
        await using var reader = await databaseCommand.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Account recovery reset returned no result.");
        }

        var resultStatus = reader.GetString(0);
        if (resultStatus != "succeeded")
        {
            throw resultStatus switch
            {
                "invalid" => RecoveryNotFound(),
                "expired" or "revoked" or "used" => RecoveryUnavailable(),
                "unavailable" => Conflict("account_unavailable", "The account is unavailable."),
                _ => new InvalidOperationException($"Unexpected account recovery result '{resultStatus}'."),
            };
        }
    }

    private async Task<PasswordIdentity?> GetPasswordIdentityAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select * from get_user_password_identity($1)");
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new PasswordIdentity(reader.GetString(0), reader.GetGuid(1), reader.GetString(2))
            : null;
    }

    private static string ValidateEmail(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 3 or > 320 ||
            normalized.Any(char.IsControl) ||
            !new EmailAddressAttribute().IsValid(normalized))
        {
            throw Validation("invalid_email", "A valid account email is required.");
        }

        return normalized;
    }

    private static void ValidateCurrentPassword(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256 || value.Any(char.IsControl))
        {
            throw Validation("current_password_invalid", "The current password is invalid.");
        }
    }

    private static string ValidateNewPassword(string value)
    {
        if (value is null ||
            value.Length is < MinimumPasswordLength or > MaximumPasswordLength ||
            value.Any(char.IsControl))
        {
            throw Validation(
                "invalid_password",
                $"Password must contain {MinimumPasswordLength} to {MaximumPasswordLength} characters without control characters.");
        }

        return value;
    }

    private static string HashRecoveryToken(string token)
    {
        if (!IdentityTokenUtility.TryHash(token, out var tokenHash))
        {
            throw RecoveryNotFound();
        }

        return tokenHash;
    }

    private static AccountSecurityException Validation(string code, string message) => new(code, message, 400);

    private static AccountSecurityException Conflict(string code, string message) => new(code, message, 409);

    private static AccountSecurityException RecoveryNotFound() => new(
        "recovery_not_found",
        "The account recovery link was not found.",
        404);

    private static AccountSecurityException RecoveryUnavailable() => new(
        "recovery_unavailable",
        "The account recovery link is no longer available.",
        410);

    private sealed record PasswordIdentity(string PasswordHash, Guid SecurityStamp, string Status);
}
