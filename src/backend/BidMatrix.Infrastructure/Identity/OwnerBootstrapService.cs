using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using BidMatrix.Database.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BidMatrix.Infrastructure.Identity;

public sealed class OwnerBootstrapService(
    BidMatrixDataSourceOptions databaseOptions,
    IConfiguration configuration,
    IPasswordHasher<AuthenticationPasswordSubject> passwordHasher,
    ILogger<OwnerBootstrapService> logger)
{
    private static readonly Guid DevelopmentOwnerUserId = Guid.Parse("01982000-0000-7000-8000-000000000001");
    private static readonly Guid DevelopmentOwnerOrganizationId = Guid.Parse("01982000-0000-7000-8000-000000000101");
    private static readonly Guid DevelopmentOwnerMembershipId = Guid.Parse("01982000-0000-7000-8000-000000000201");
    private static readonly Regex SlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 128;

    public async Task<bool> SynchronizeDevelopmentOwnerAsync(CancellationToken cancellationToken = default)
    {
        var input = ReadInput(
            "BidMatrix Development Organization",
            "bidmatrix-development");
        var subject = new AuthenticationPasswordSubject(DevelopmentOwnerUserId);
        await using var dataSource = NpgsqlDataSource.Create(databaseOptions.BuildMigrationConnectionString());
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await UpsertDevelopmentOwnerAsync(connection, transaction, input, cancellationToken);
        await EnsureDevelopmentOrganizationAsync(connection, transaction, input, cancellationToken);
        await EnsureMembershipAsync(
            connection,
            transaction,
            DevelopmentOwnerMembershipId,
            DevelopmentOwnerOrganizationId,
            DevelopmentOwnerUserId,
            cancellationToken);
        await EnsurePlatformRoleAsync(connection, transaction, DevelopmentOwnerUserId, cancellationToken);
        var credentialChanged = await SynchronizeCredentialAsync(
            connection,
            transaction,
            subject,
            input.Password,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return credentialChanged;
    }

    public async Task BootstrapInitialOwnerAsync(CancellationToken cancellationToken = default)
    {
        var input = ReadInput(null, null);
        var userId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();
        var createdAt = DateTimeOffset.UtcNow;
        var subject = new AuthenticationPasswordSubject(userId);
        var passwordHash = passwordHasher.HashPassword(subject, input.Password);

        await using var dataSource = NpgsqlDataSource.Create(databaseOptions.BuildMigrationConnectionString());
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "select pg_advisory_xact_lock(49089055130011)";
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureNoPlatformOwnerAsync(connection, transaction, cancellationToken);
        await EnsureBootstrapIdentityAvailableAsync(connection, transaction, input, cancellationToken);
        await InsertOwnerAsync(
            connection,
            transaction,
            userId,
            input,
            createdAt,
            cancellationToken);
        await InsertOrganizationAsync(
            connection,
            transaction,
            organizationId,
            input,
            createdAt,
            cancellationToken);
        await EnsureMembershipAsync(
            connection,
            transaction,
            membershipId,
            organizationId,
            userId,
            cancellationToken);
        await EnsurePlatformRoleAsync(connection, transaction, userId, cancellationToken);
        await InsertCredentialAsync(
            connection,
            transaction,
            userId,
            passwordHash,
            createdAt,
            cancellationToken);
        await AppendBootstrapAuditAsync(
            connection,
            transaction,
            userId,
            organizationId,
            createdAt,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Created the initial platform owner {UserId} and organization {OrganizationId} with the one-shot bootstrap command.",
            userId,
            organizationId);
    }

    private BootstrapInput ReadInput(string? defaultOrganizationName, string? defaultOrganizationSlug)
    {
        var email = RequireConfiguration("OWNER_BOOTSTRAP_EMAIL").Trim();
        var password = RequireConfiguration("OWNER_BOOTSTRAP_PASSWORD");
        var displayName = configuration["OWNER_BOOTSTRAP_DISPLAY_NAME"]?.Trim();
        var organizationName = configuration["OWNER_BOOTSTRAP_ORGANIZATION_NAME"]?.Trim()
            ?? defaultOrganizationName
            ?? throw new InvalidOperationException("OWNER_BOOTSTRAP_ORGANIZATION_NAME is required.");
        var organizationSlug = configuration["OWNER_BOOTSTRAP_ORGANIZATION_SLUG"]?.Trim().ToLowerInvariant()
            ?? defaultOrganizationSlug
            ?? throw new InvalidOperationException("OWNER_BOOTSTRAP_ORGANIZATION_SLUG is required.");

        if (!new EmailAddressAttribute().IsValid(email) || email.Any(char.IsControl))
        {
            throw new InvalidOperationException("OWNER_BOOTSTRAP_EMAIL must be a valid email address.");
        }

        if (password.Length is < MinimumPasswordLength or > MaximumPasswordLength || password.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"OWNER_BOOTSTRAP_PASSWORD must contain {MinimumPasswordLength} to {MaximumPasswordLength} characters without control characters.");
        }

        if (displayName is { Length: > 120 } || displayName?.Any(char.IsControl) == true)
        {
            throw new InvalidOperationException(
                "OWNER_BOOTSTRAP_DISPLAY_NAME must contain at most 120 characters without control characters.");
        }

        if (organizationName.Length is < 2 or > 120 || organizationName.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "OWNER_BOOTSTRAP_ORGANIZATION_NAME must contain 2 to 120 characters without control characters.");
        }

        if (organizationSlug.Length is < 3 or > 63 || !SlugPattern.IsMatch(organizationSlug))
        {
            throw new InvalidOperationException(
                "OWNER_BOOTSTRAP_ORGANIZATION_SLUG must contain 3 to 63 lowercase letters, numbers, or single hyphens.");
        }

        return new BootstrapInput(
            email,
            email.ToUpperInvariant(),
            password,
            displayName,
            organizationName,
            organizationSlug);
    }

    private string RequireConfiguration(string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required.");

    private static async Task UpsertDevelopmentOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BootstrapInput input,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into users (
                id, email, normalized_email, display_name, status, created_at, updated_at
            )
            values ($1, $2, $3, $4, 'active', now(), now())
            on conflict (id) do update
            set email = excluded.email,
                normalized_email = excluded.normalized_email,
                display_name = excluded.display_name,
                status = 'active',
                updated_at = now()
            """;
        command.Parameters.AddWithValue(DevelopmentOwnerUserId);
        command.Parameters.AddWithValue(input.Email);
        command.Parameters.AddWithValue(input.NormalizedEmail);
        command.Parameters.AddWithValue((object?)input.DisplayName ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDevelopmentOrganizationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BootstrapInput input,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into organizations (id, name, slug, status, created_at, updated_at)
            values ($1, $2, $3, 'active', now(), now())
            on conflict (id) do update
            set name = excluded.name,
                slug = excluded.slug,
                status = 'active',
                updated_at = now()
            """;
        command.Parameters.AddWithValue(DevelopmentOwnerOrganizationId);
        command.Parameters.AddWithValue(input.OrganizationName);
        command.Parameters.AddWithValue(input.OrganizationSlug);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureMembershipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid membershipId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into organization_memberships (id, organization_id, user_id, role, created_at)
            values ($1, $2, $3, 'owner', now())
            on conflict (organization_id, user_id) do update set role = 'owner'
            """;
        command.Parameters.AddWithValue(membershipId);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsurePlatformRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into user_platform_roles (user_id, role, created_at)
            values ($1, 'platform_owner', now())
            on conflict (user_id, role) do nothing
            """;
        command.Parameters.AddWithValue(userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> SynchronizeCredentialAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuthenticationPasswordSubject subject,
        string password,
        CancellationToken cancellationToken)
    {
        string? existingPasswordHash;
        await using (var existingCommand = connection.CreateCommand())
        {
            existingCommand.Transaction = transaction;
            existingCommand.CommandText = "select password_hash from user_credentials where user_id = $1 for update";
            existingCommand.Parameters.AddWithValue(subject.UserId);
            existingPasswordHash = (string?)await existingCommand.ExecuteScalarAsync(cancellationToken);
        }

        if (existingPasswordHash is not null &&
            passwordHasher.VerifyHashedPassword(subject, existingPasswordHash, password) is not PasswordVerificationResult.Failed)
        {
            return false;
        }

        var passwordHash = passwordHasher.HashPassword(subject, password);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into user_credentials (
                user_id, password_hash, failed_access_count, lockout_end, security_stamp,
                password_changed_at, created_at, updated_at, version
            )
            values ($1, $2, 0, null, $3, now(), now(), now(), 1)
            on conflict (user_id) do update
            set password_hash = excluded.password_hash,
                failed_access_count = 0,
                lockout_end = null,
                security_stamp = excluded.security_stamp,
                password_changed_at = now(),
                updated_at = now(),
                version = user_credentials.version + 1
            """;
        command.Parameters.AddWithValue(subject.UserId);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static async Task EnsureNoPlatformOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select exists(select 1 from user_platform_roles where role = 'platform_owner')";
        if ((bool)(await command.ExecuteScalarAsync(cancellationToken) ?? true))
        {
            throw new InvalidOperationException(
                "Initial owner bootstrap refused because a platform owner already exists.");
        }
    }

    private static async Task EnsureBootstrapIdentityAvailableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BootstrapInput input,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select
                exists(select 1 from users where normalized_email = $1),
                exists(select 1 from organizations where slug = $2)
            """;
        command.Parameters.AddWithValue(input.NormalizedEmail);
        command.Parameters.AddWithValue(input.OrganizationSlug);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        _ = await reader.ReadAsync(cancellationToken);

        if (reader.GetBoolean(0))
        {
            throw new InvalidOperationException("Initial owner bootstrap email is already registered.");
        }

        if (reader.GetBoolean(1))
        {
            throw new InvalidOperationException("Initial owner bootstrap organization slug is already registered.");
        }
    }

    private static async Task InsertOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        BootstrapInput input,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into users (id, email, normalized_email, display_name, status, created_at, updated_at)
            values ($1, $2, $3, $4, 'active', $5, $5)
            """;
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(input.Email);
        command.Parameters.AddWithValue(input.NormalizedEmail);
        command.Parameters.AddWithValue((object?)input.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue(createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOrganizationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        BootstrapInput input,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into organizations (id, name, slug, status, created_at, updated_at)
            values ($1, $2, $3, 'active', $4, $4)
            """;
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(input.OrganizationName);
        command.Parameters.AddWithValue(input.OrganizationSlug);
        command.Parameters.AddWithValue(createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCredentialAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        string passwordHash,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into user_credentials (
                user_id, password_hash, failed_access_count, lockout_end, security_stamp,
                password_changed_at, created_at, updated_at, version
            )
            values ($1, $2, 0, null, $3, $4, $4, $4, 1)
            """;
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AppendBootstrapAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        Guid organizationId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id
            from append_audit_event(
                $1, 'system', 'owner-bootstrap-command', 'identity.owner.bootstrapped',
                'user', $2, $3, null, null, 'Created initial platform owner',
                jsonb_build_object('userId', $2::uuid, 'organizationId', $3::uuid), $4
            )
            """;
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(userId.ToString());
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(createdAt);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private sealed record BootstrapInput(
        string Email,
        string NormalizedEmail,
        string Password,
        string? DisplayName,
        string OrganizationName,
        string OrganizationSlug);
}
