using System.ComponentModel.DataAnnotations;
using BidMatrix.Database.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BidMatrix.Infrastructure.Identity;

internal sealed class DevelopmentOwnerBootstrapHostedService(
    BidMatrixDataSourceOptions databaseOptions,
    IConfiguration configuration,
    IHostEnvironment environment,
    IPasswordHasher<AuthenticationPasswordSubject> passwordHasher,
    ILogger<DevelopmentOwnerBootstrapHostedService> logger) : IHostedService
{
    private static readonly Guid OwnerUserId = Guid.Parse("01982000-0000-7000-8000-000000000001");
    private static readonly Guid OwnerOrganizationId = Guid.Parse("01982000-0000-7000-8000-000000000101");
    private static readonly Guid OwnerMembershipId = Guid.Parse("01982000-0000-7000-8000-000000000201");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var email = RequireConfiguration("OWNER_BOOTSTRAP_EMAIL").Trim();
        var password = RequireConfiguration("OWNER_BOOTSTRAP_PASSWORD");
        var displayName = configuration["OWNER_BOOTSTRAP_DISPLAY_NAME"]?.Trim();

        if (!new EmailAddressAttribute().IsValid(email))
        {
            throw new InvalidOperationException("OWNER_BOOTSTRAP_EMAIL must be a valid email address.");
        }

        var normalizedEmail = email.ToUpperInvariant();
        var subject = new AuthenticationPasswordSubject(OwnerUserId);
        await using var dataSource = NpgsqlDataSource.Create(databaseOptions.BuildMigrationConnectionString());
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await UpsertOwnerAsync(connection, transaction, email, normalizedEmail, displayName, cancellationToken);
        await EnsureOwnerOrganizationAsync(connection, transaction, cancellationToken);
        await EnsureOwnerMembershipAsync(connection, transaction, cancellationToken);
        await EnsurePlatformRoleAsync(connection, transaction, cancellationToken);
        var credentialChanged = await SynchronizeCredentialAsync(
            connection,
            transaction,
            subject,
            password,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            credentialChanged
                ? "Development owner credential was synchronized from environment configuration."
                : "Development owner credential already matches environment configuration.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string RequireConfiguration(string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required in Development.");

    private static async Task UpsertOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string email,
        string normalizedEmail,
        string? displayName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into users (
                id,
                email,
                normalized_email,
                display_name,
                status,
                created_at,
                updated_at
            )
            values ($1, $2, $3, $4, 'active', now(), now())
            on conflict (id) do update
            set email = excluded.email,
                normalized_email = excluded.normalized_email,
                display_name = excluded.display_name,
                status = 'active',
                updated_at = now()
            """;
        command.Parameters.AddWithValue(OwnerUserId);
        command.Parameters.AddWithValue(email);
        command.Parameters.AddWithValue(normalizedEmail);
        command.Parameters.AddWithValue((object?)displayName ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureOwnerOrganizationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into organizations (id, name, slug, status, created_at, updated_at)
            values ($1, 'BidMatrix Development Organization', 'bidmatrix-development', 'active', now(), now())
            on conflict (id) do nothing
            """;
        command.Parameters.AddWithValue(OwnerOrganizationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureOwnerMembershipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into organization_memberships (id, organization_id, user_id, role, created_at)
            values ($1, $2, $3, 'owner', now())
            on conflict (organization_id, user_id) do update set role = 'owner'
            """;
        command.Parameters.AddWithValue(OwnerMembershipId);
        command.Parameters.AddWithValue(OwnerOrganizationId);
        command.Parameters.AddWithValue(OwnerUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsurePlatformRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into user_platform_roles (user_id, role, created_at)
            values ($1, 'platform_owner', now())
            on conflict (user_id, role) do nothing
            """;
        command.Parameters.AddWithValue(OwnerUserId);
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
            existingCommand.Parameters.AddWithValue(OwnerUserId);
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
                user_id,
                password_hash,
                failed_access_count,
                lockout_end,
                security_stamp,
                password_changed_at,
                created_at,
                updated_at,
                version
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
        command.Parameters.AddWithValue(OwnerUserId);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }
}
