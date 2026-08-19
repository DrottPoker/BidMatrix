using System.Net;
using System.Net.Http.Json;
using BidMatrix.Application.Identity;
using BidMatrix.Contracts.Identity;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class FederatedIdentityTests(DatabaseFixture database)
{
    private const string OwnerEmail = "owner@example.invalid";
    private const string OwnerPassword = "phase-three-owner-password";

    [Fact]
    public async Task SelfServiceRegistrationCreatesPrivateOwnerWorkspaceWithoutPlatformAuthority()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var scope = factory.Services.CreateScope();
        var identityService = scope.ServiceProvider.GetRequiredService<IFederatedIdentityService>();
        var issuer = $"https://registration-{Guid.NewGuid():N}.example.invalid";
        var email = $"self-service-{Guid.NewGuid():N}@example.invalid";
        var profile = new FederatedIdentityProfile(
            issuer,
            FederatedSubjectUtility.Hash(issuer, $"subject-{Guid.NewGuid():N}"),
            email,
            DateTimeOffset.UtcNow,
            "Self Service Owner");
        var registrationStartedAt = DateTimeOffset.UtcNow;

        var registered = await identityService.RegisterAsync(
            new RegisterFederatedAccountCommand(
                "Test managed identity",
                profile,
                "self-service-registration-test"));

        Assert.Equal(email, registered.User.Email);
        Assert.Equal("Self Service Owner", registered.User.DisplayName);
        Assert.Single(registered.User.Memberships);
        Assert.Equal("owner", registered.User.Memberships[0].Role);
        Assert.Empty(registered.User.PlatformRoles);

        var authenticated = await identityService.AuthenticateAsync(
            profile,
            "self-service-login-test");
        Assert.Equal(registered.User.UserId, authenticated.User.UserId);
        Assert.Equal(registered.IdentityId, authenticated.IdentityId);

        await using (var connection = await database.MigrationDataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select organization.status,
                       membership.role,
                       credential.password_enabled,
                       count(audit.id),
                       (select count(*) from agent_runs where started_at >= $2)
                from users user_record
                join organization_memberships membership on membership.user_id = user_record.id
                join organizations organization on organization.id = membership.organization_id
                join user_credentials credential on credential.user_id = user_record.id
                left join audit_events audit
                  on audit.action = 'account.self_service.registered'
                 and audit.target_id = user_record.id::text
                where user_record.id = $1
                group by organization.status, membership.role, credential.password_enabled
                """;
            command.Parameters.AddWithValue(registered.User.UserId);
            command.Parameters.AddWithValue(registrationStartedAt);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("active", reader.GetString(0));
            Assert.Equal("owner", reader.GetString(1));
            Assert.False(reader.GetBoolean(2));
            Assert.Equal(1, reader.GetInt64(3));
            Assert.Equal(0, reader.GetInt64(4));
        }

        var conflictingProfile = profile with
        {
            SubjectHash = FederatedSubjectUtility.Hash(
                issuer,
                $"other-subject-{Guid.NewGuid():N}"),
        };
        var conflict = await Assert.ThrowsAsync<FederatedIdentityException>(() =>
            identityService.RegisterAsync(
                new RegisterFederatedAccountCommand(
                    "Test managed identity",
                    conflictingProfile,
                    "self-service-email-conflict-test")));
        Assert.Equal("email_registered", conflict.Code);
    }

    [Fact]
    public async Task FederatedIdentityRequiresExplicitLinkAndNeverStoresRawSubject()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        await LoginAsync(client);
        var owner = await ReadOwnerSessionAsync();

        using var scope = factory.Services.CreateScope();
        var identityService = scope.ServiceProvider.GetRequiredService<IFederatedIdentityService>();
        var sessionService = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var issuer = $"https://identity-{Guid.NewGuid():N}.example.invalid";
        var subject = $"provider-subject-{Guid.NewGuid():N}";
        var subjectHash = FederatedSubjectUtility.Hash(issuer, subject);
        var authenticatedAt = DateTimeOffset.UtcNow;
        var profile = new FederatedIdentityProfile(
            issuer,
            subjectHash,
            OwnerEmail,
            authenticatedAt);

        var notLinked = await Assert.ThrowsAsync<FederatedIdentityException>(() =>
            identityService.AuthenticateAsync(
                profile with
                {
                    SubjectHash = FederatedSubjectUtility.Hash(
                        issuer,
                        $"unlinked-{Guid.NewGuid():N}"),
                },
                "federated-unlinked-test"));
        Assert.Equal("identity_not_linked", notLinked.Code);

        var emailMismatch = await Assert.ThrowsAsync<FederatedIdentityException>(() =>
            identityService.LinkAsync(new LinkFederatedIdentityCommand(
                owner.UserId,
                owner.SessionId,
                owner.SecurityStamp,
                "Test managed identity",
                profile with { Email = "different@example.invalid" },
                "federated-email-test")));
        Assert.Equal("email_mismatch", emailMismatch.Code);

        var linked = await identityService.LinkAsync(new LinkFederatedIdentityCommand(
            owner.UserId,
            owner.SessionId,
            owner.SecurityStamp,
            "Test managed identity",
            profile,
            "federated-link-test"));
        Assert.Equal(owner.UserId, linked.User.UserId);

        var stored = await ReadStoredIdentityAsync(linked.IdentityId);
        Assert.Matches("^[0-9a-f]{64}$", stored.SubjectHash);
        Assert.Equal(subjectHash, stored.SubjectHash);
        Assert.DoesNotContain(subject, stored.SubjectHash, StringComparison.Ordinal);
        Assert.DoesNotContain(subject, stored.AuditMetadata, StringComparison.Ordinal);

        var ownerLogin = await identityService.AuthenticateAsync(
            profile,
            "federated-login-test");
        Assert.Equal(owner.UserId, ownerLogin.User.UserId);

        var secondUser = await CreateUserAsync();
        var secondSession = await sessionService.CreateAsync(
            secondUser.UserId,
            "federated-conflict-session");
        var conflict = await Assert.ThrowsAsync<FederatedIdentityException>(() =>
            identityService.LinkAsync(new LinkFederatedIdentityCommand(
                secondUser.UserId,
                secondSession.Id,
                secondUser.SecurityStamp,
                "Test managed identity",
                profile with { Email = secondUser.Email },
                "federated-conflict-test")));
        Assert.Equal("identity_conflict", conflict.Code);

        using var listResponse = await client.GetAsync("/v1/auth/federated-identities");
        listResponse.EnsureSuccessStatusCode();
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(subject, listJson, StringComparison.Ordinal);
        var list = await listResponse.Content.ReadFromJsonAsync<FederatedIdentityListResponse>();
        Assert.NotNull(list);
        Assert.True(list.NativePasswordEnabled);
        Assert.Contains(list.Identities, identity => identity.Id == linked.IdentityId.ToString());

        var lastMethod = await Assert.ThrowsAsync<FederatedIdentityException>(() =>
            identityService.RevokeAsync(new RevokeFederatedIdentityCommand(
                linked.IdentityId,
                owner.UserId,
                false,
                "federated-last-method-test")));
        Assert.Equal("last_authentication_method", lastMethod.Code);

        await AddCsrfTokenAsync(client);
        using var revokeResponse = await client.PostAsync(
            $"/v1/auth/federated-identities/{linked.IdentityId}/revoke",
            null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<RevokeFederatedIdentityResponse>();
        Assert.NotNull(revoked);
        Assert.Equal("revoked", revoked.Identity.Status);
        Assert.True(revoked.RevokedSessionCount >= 1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/me")).StatusCode);
        Assert.False(await identityService.ValidateAsync(linked.IdentityId, owner.UserId));
    }

    private async Task<(Guid UserId, Guid SessionId, Guid SecurityStamp)> ReadOwnerSessionAsync()
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select user_record.id, session_record.id, credential.security_stamp
            from users user_record
            join user_credentials credential on credential.user_id = user_record.id
            join user_sessions session_record on session_record.user_id = user_record.id
            where user_record.normalized_email = upper($1)
              and session_record.revoked_at is null
            order by session_record.created_at desc
            limit 1
            """;
        command.Parameters.AddWithValue(OwnerEmail);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2));
    }

    private async Task<(string SubjectHash, string AuditMetadata)> ReadStoredIdentityAsync(Guid identityId)
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select identity.subject_hash,
                   coalesce(string_agg(audit.metadata::text, ' '), '')
            from user_federated_identities identity
            left join audit_events audit
              on audit.target_type = 'federated_identity'
             and audit.target_id = identity.id::text
            where identity.id = $1
            group by identity.subject_hash
            """;
        command.Parameters.AddWithValue(identityId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    private async Task<(Guid UserId, Guid SecurityStamp, string Email)> CreateUserAsync()
    {
        var userId = Guid.CreateVersion7();
        var securityStamp = Guid.CreateVersion7();
        var email = $"federated-{userId:N}@example.invalid";
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = """
                insert into users (id, email, normalized_email, status, created_at, updated_at)
                values ($1, $2, upper($2), 'active', now(), now())
                """;
            userCommand.Parameters.AddWithValue(userId);
            userCommand.Parameters.AddWithValue(email);
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (var credentialCommand = connection.CreateCommand())
        {
            credentialCommand.Transaction = transaction;
            credentialCommand.CommandText = """
                insert into user_credentials (
                    user_id,
                    password_hash,
                    security_stamp,
                    password_changed_at,
                    created_at,
                    updated_at,
                    version
                )
                values ($1, 'not-used', $2, now(), now(), now(), 1)
                """;
            credentialCommand.Parameters.AddWithValue(userId);
            credentialCommand.Parameters.AddWithValue(securityStamp);
            await credentialCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return (userId, securityStamp, email);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        await AddCsrfTokenAsync(client);
        using var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new LoginRequest(OwnerEmail, OwnerPassword));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AddCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Remove(token.HeaderName);
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);
    }
}
