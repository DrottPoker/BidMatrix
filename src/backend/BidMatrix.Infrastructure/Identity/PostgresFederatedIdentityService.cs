using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using BidMatrix.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace BidMatrix.Infrastructure.Identity;

internal sealed class PostgresFederatedIdentityService(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider,
    IdentitySecurityOptions securityOptions,
    ManagedOidcOptions managedOidcOptions,
    IPasswordHasher<AuthenticationPasswordSubject> passwordHasher) : IFederatedIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(1);
    private const int MaximumIdentityCount = 20;

    public async Task<AuthenticatedFederatedIdentity> AuthenticateAsync(
        FederatedIdentityProfile profile,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);

        await using var command = dataSource.CreateCommand(
            "select * from resolve_federated_identity($1, $2, $3)");
        command.Parameters.AddWithValue(profile.Issuer);
        command.Parameters.AddWithValue(profile.SubjectHash);
        command.Parameters.AddWithValue(profile.AuthenticatedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Federated identity resolution returned no result.");
        }

        var status = reader.GetString(0);
        if (status != "succeeded")
        {
            throw CreateException(status);
        }

        var memberships = JsonSerializer.Deserialize<List<MembershipPayload>>(reader.GetString(6), JsonOptions) ?? [];
        var platformRoles = JsonSerializer.Deserialize<List<string>>(reader.GetString(7), JsonOptions) ?? [];
        var user = new AuthenticatedUser(
            reader.GetGuid(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetGuid(5),
            memberships
                .Select(item => new OrganizationMembership(item.OrganizationId, item.Role))
                .ToArray(),
            platformRoles);

        return new AuthenticatedFederatedIdentity(reader.GetGuid(1), user);
    }

    public async Task<AuthenticatedFederatedIdentity> LinkAsync(
        LinkFederatedIdentityCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(command.Profile);
        var providerName = ValidateProviderName(command.ProviderName);

        await using var databaseCommand = dataSource.CreateCommand(
            "select * from link_federated_identity($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)");
        databaseCommand.Parameters.AddWithValue(Guid.CreateVersion7());
        databaseCommand.Parameters.AddWithValue(command.UserId);
        databaseCommand.Parameters.AddWithValue(command.SessionId);
        databaseCommand.Parameters.AddWithValue(command.SecurityStamp);
        databaseCommand.Parameters.AddWithValue(providerName);
        databaseCommand.Parameters.AddWithValue(command.Profile.Issuer);
        databaseCommand.Parameters.AddWithValue(command.Profile.SubjectHash);
        databaseCommand.Parameters.AddWithValue(command.Profile.Email);
        databaseCommand.Parameters.AddWithValue(command.Profile.AuthenticatedAt);
        databaseCommand.Parameters.AddWithValue(securityOptions.SessionIdleTimeout);
        databaseCommand.Parameters.AddWithValue(command.TraceId);
        await using var reader = await databaseCommand.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Federated identity linking returned no result.");
        }

        var status = reader.GetString(0);
        if (status is not ("linked" or "already_linked"))
        {
            throw CreateException(status);
        }

        await reader.CloseAsync();
        return await AuthenticateAsync(command.Profile, command.TraceId, cancellationToken);
    }

    public async Task<AuthenticatedFederatedIdentity> RegisterAsync(
        RegisterFederatedAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(command.Profile);
        var providerName = ValidateProviderName(command.ProviderName);
        var displayName = NormalizeDisplayName(command.Profile.DisplayName, command.Profile.Email);
        var userId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();
        var identityId = Guid.CreateVersion7();
        var securityStamp = Guid.CreateVersion7();
        var inaccessiblePassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var disabledPasswordHash = passwordHasher.HashPassword(
            new AuthenticationPasswordSubject(userId),
            inaccessiblePassword);
        var organizationName = $"{displayName}'s workspace";
        var organizationSlug = $"workspace-{organizationId:N}";

        await using var databaseCommand = dataSource.CreateCommand("""
            select register_federated_account(
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15
            )
            """);
        databaseCommand.Parameters.AddWithValue(userId);
        databaseCommand.Parameters.AddWithValue(organizationId);
        databaseCommand.Parameters.AddWithValue(membershipId);
        databaseCommand.Parameters.AddWithValue(identityId);
        databaseCommand.Parameters.AddWithValue(providerName);
        databaseCommand.Parameters.AddWithValue(command.Profile.Issuer);
        databaseCommand.Parameters.AddWithValue(command.Profile.SubjectHash);
        databaseCommand.Parameters.AddWithValue(command.Profile.Email.Trim());
        databaseCommand.Parameters.AddWithValue(displayName);
        databaseCommand.Parameters.AddWithValue(organizationName);
        databaseCommand.Parameters.AddWithValue(organizationSlug);
        databaseCommand.Parameters.AddWithValue(disabledPasswordHash);
        databaseCommand.Parameters.AddWithValue(securityStamp);
        databaseCommand.Parameters.AddWithValue(command.Profile.AuthenticatedAt);
        databaseCommand.Parameters.AddWithValue(command.TraceId);

        var status = (string?)(await databaseCommand.ExecuteScalarAsync(cancellationToken))
            ?? throw new InvalidOperationException("Self-service account registration returned no result.");
        if (status is not ("registered" or "already_registered"))
        {
            throw CreateException(status);
        }

        return await AuthenticateAsync(command.Profile, command.TraceId, cancellationToken);
    }

    public async Task<IReadOnlyList<FederatedIdentityRecord>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "select * from list_federated_identities($1, $2)");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(MaximumIdentityCount);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var identities = new List<FederatedIdentityRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            identities.Add(ReadIdentity(reader));
        }

        return identities;
    }

    public async Task<bool> HasNativePasswordAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "select user_has_native_password($1)");
        command.Parameters.AddWithValue(userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<RevokedFederatedIdentity> RevokeAsync(
        RevokeFederatedIdentityCommand command,
        CancellationToken cancellationToken = default)
    {
        var nativeLoginAvailable = command.NativeLoginEnabled &&
                                   await HasNativePasswordAsync(command.UserId, cancellationToken);
        await using var databaseCommand = dataSource.CreateCommand(
            "select * from revoke_federated_identity($1, $2, $3, $4, $5)");
        databaseCommand.Parameters.AddWithValue(command.IdentityId);
        databaseCommand.Parameters.AddWithValue(command.UserId);
        databaseCommand.Parameters.AddWithValue(nativeLoginAvailable);
        databaseCommand.Parameters.AddWithValue(timeProvider.GetUtcNow());
        databaseCommand.Parameters.AddWithValue(command.TraceId);
        await using var reader = await databaseCommand.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Federated identity revocation returned no result.");
        }

        var status = reader.GetString(0);
        if (status is not ("revoked" or "already_revoked"))
        {
            throw CreateException(status);
        }

        return new RevokedFederatedIdentity(
            new FederatedIdentityRecord(
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetInt32(9)),
            reader.GetInt32(10));
    }

    public async Task<bool> ValidateAsync(
        Guid identityId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "select validate_federated_identity($1, $2)");
        command.Parameters.AddWithValue(identityId);
        command.Parameters.AddWithValue(userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private void ValidateProfile(FederatedIdentityProfile profile)
    {
        if (!Uri.TryCreate(profile.Issuer, UriKind.Absolute, out var issuer) ||
            (issuer.Scheme != Uri.UriSchemeHttps && issuer.Scheme != Uri.UriSchemeHttp) ||
            (managedOidcOptions.Authority?.Scheme == Uri.UriSchemeHttps &&
             issuer.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(issuer.UserInfo) ||
            !string.IsNullOrEmpty(issuer.Query) ||
            !string.IsNullOrEmpty(issuer.Fragment) ||
            profile.Issuer.Length > 2048 ||
            string.IsNullOrEmpty(profile.SubjectHash) ||
            profile.SubjectHash.Length != 64 ||
            profile.SubjectHash.Any(character => character is not (>= '0' and <= '9') and
                                                 not (>= 'a' and <= 'f')) ||
            profile.Email.Length > 320 ||
            profile.Email.Any(char.IsControl) ||
            !new EmailAddressAttribute().IsValid(profile.Email))
        {
            throw new FederatedIdentityException(
                "invalid_identity",
                "The managed identity claims are invalid.",
                400);
        }

        var now = timeProvider.GetUtcNow();
        if (profile.AuthenticatedAt > now.Add(AllowedClockSkew) ||
            profile.AuthenticatedAt.Add(securityOptions.RecentAuthenticationLifetime) <= now)
        {
            throw new FederatedIdentityException(
                "authentication_too_old",
                "The managed identity authentication is not recent enough.",
                401);
        }
    }

    private static string ValidateProviderName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 80 || normalized.Any(char.IsControl))
        {
            throw new FederatedIdentityException(
                "invalid_provider",
                "The managed identity provider is invalid.",
                400);
        }

        return normalized;
    }

    private static string NormalizeDisplayName(string? value, string email)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = email.Split('@', 2)[0];
        }

        if (normalized.Length > 100)
        {
            normalized = normalized[..100];
        }

        if (normalized.Length < 2 || normalized.Any(char.IsControl))
        {
            return "BidMatrix user";
        }

        return normalized;
    }

    private static FederatedIdentityRecord ReadIdentity(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetFieldValue<DateTimeOffset>(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
        reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
        reader.GetInt32(8));

    private static FederatedIdentityException CreateException(string status) => status switch
    {
        "not_linked" => new FederatedIdentityException(
            "identity_not_linked",
            "This managed identity is not linked to a BidMatrix account.",
            401),
        "revoked" => new FederatedIdentityException(
            "identity_revoked",
            "This managed identity link has been revoked.",
            401),
        "email_mismatch" => new FederatedIdentityException(
            "email_mismatch",
            "The managed identity email does not match the BidMatrix account.",
            409),
        "identity_conflict" => new FederatedIdentityException(
            "identity_conflict",
            "The managed identity is already linked to another account.",
            409),
        "email_registered" => new FederatedIdentityException(
            "email_registered",
            "An existing BidMatrix account already uses this email address.",
            409),
        "slug_conflict" => new FederatedIdentityException(
            "workspace_unavailable",
            "The private workspace could not be created.",
            409),
        "session_invalid" => new FederatedIdentityException(
            "session_invalid",
            "The session used to link the managed identity is no longer valid.",
            401),
        "last_authentication_method" => new FederatedIdentityException(
            "last_authentication_method",
            "The last available authentication method cannot be revoked.",
            409),
        "not_found" => new FederatedIdentityException(
            "identity_not_found",
            "The managed identity link was not found.",
            404),
        "invalid" => new FederatedIdentityException(
            "invalid_identity",
            "The managed identity request is invalid.",
            400),
        _ => new FederatedIdentityException(
            "identity_unavailable",
            "The managed identity is unavailable.",
            401),
    };

    private sealed record MembershipPayload(Guid OrganizationId, string Role);
}
