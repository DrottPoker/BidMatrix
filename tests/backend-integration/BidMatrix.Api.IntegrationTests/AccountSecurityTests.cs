using System.Net;
using System.Net.Http.Json;
using BidMatrix.Application.Identity;
using BidMatrix.Contracts.Identity;
using BidMatrix.Database.Schema;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AccountSecurityTests(DatabaseFixture database)
{
    private const string OwnerEmail = "owner@example.invalid";
    private const string OwnerPassword = "phase-three-owner-password";

    [Fact]
    public async Task SessionsAreListedWithoutSecretsAndRevocationIsAccountScoped()
    {
        var firstAccount = await CreateCustomerAsync("sessions-a");
        var secondAccount = await CreateCustomerAsync("sessions-b");
        using var factory = new BidMatrixApiFactory(database);
        using var firstClient = CreateCookieClient(factory);
        using var secondClient = CreateCookieClient(factory);
        using var unrelatedClient = CreateCookieClient(factory);
        await LoginAsync(firstClient, firstAccount.Email, firstAccount.Password);
        await LoginAsync(secondClient, firstAccount.Email, firstAccount.Password);
        await LoginAsync(unrelatedClient, secondAccount.Email, secondAccount.Password);

        using var listResponse = await firstClient.GetAsync("/v1/auth/sessions");
        listResponse.EnsureSuccessStatusCode();
        var responseText = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", responseText, StringComparison.OrdinalIgnoreCase);
        var sessions = await listResponse.Content.ReadFromJsonAsync<UserSessionListResponse>();
        Assert.NotNull(sessions);
        Assert.Equal(2, sessions.Sessions.Count);
        Assert.Single(sessions.Sessions, session => session.IsCurrent);
        var otherSession = Assert.Single(sessions.Sessions, session => !session.IsCurrent);

        await AddCsrfTokenAsync(firstClient);
        using var revokeResponse = await firstClient.PostAsync(
            $"/v1/auth/sessions/{otherSession.Id}/revoke",
            null);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync("/v1/me")).StatusCode);

        using var unrelatedSessionsResponse = await unrelatedClient.GetAsync("/v1/auth/sessions");
        var unrelatedSessions = await unrelatedSessionsResponse.Content.ReadFromJsonAsync<UserSessionListResponse>();
        var unrelatedSession = Assert.Single(Assert.IsType<UserSessionListResponse>(unrelatedSessions).Sessions);
        using var crossAccountResponse = await firstClient.PostAsync(
            $"/v1/auth/sessions/{unrelatedSession.Id}/revoke",
            null);
        Assert.Equal(HttpStatusCode.NotFound, crossAccountResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await unrelatedClient.GetAsync("/v1/me")).StatusCode);

        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select count(*)
            from user_sessions
            where user_id = $1
              and token_hash ~ '^[0-9a-f]{64}$'
            """;
        command.Parameters.AddWithValue(firstAccount.UserId);
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task PasswordChangeRevokesEverySessionAndRequiresTheNewPassword()
    {
        var account = await CreateCustomerAsync("password-change");
        const string newPassword = "new-customer-password-2026";
        using var factory = new BidMatrixApiFactory(database);
        using var firstClient = CreateCookieClient(factory);
        using var secondClient = CreateCookieClient(factory);
        await LoginAsync(firstClient, account.Email, account.Password);
        await LoginAsync(secondClient, account.Email, account.Password);

        await AddCsrfTokenAsync(firstClient);
        using var changeResponse = await firstClient.PostAsJsonAsync(
            "/v1/auth/password",
            new ChangePasswordRequest(account.Password, newPassword));
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await firstClient.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync("/v1/me")).StatusCode);

        using var oldPasswordClient = CreateCookieClient(factory);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LoginResponseAsync(oldPasswordClient, account.Email, account.Password)).StatusCode);
        using var newPasswordClient = CreateCookieClient(factory);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginResponseAsync(newPasswordClient, account.Email, newPassword)).StatusCode);

        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        Assert.Equal(2, await CountAsync(
            connection,
            "select count(*) from user_sessions where user_id = $1 and revoked_reason = 'password_changed'",
            account.UserId));
        Assert.Equal(1, await CountAsync(
            connection,
            "select count(*) from audit_events where action = 'identity.password.changed' and target_id = $1",
            account.UserId.ToString()));
    }

    [Fact]
    public async Task PasswordPolicyAcceptsEightCharactersAndRejectsSeven()
    {
        var account = await CreateCustomerAsync("password-length");
        using var factory = new BidMatrixApiFactory(database);
        using var client = CreateCookieClient(factory);
        await LoginAsync(client, account.Email, account.Password);
        await AddCsrfTokenAsync(client);

        using var tooShortResponse = await client.PostAsJsonAsync(
            "/v1/auth/password",
            new ChangePasswordRequest(account.Password, "1234567"));
        Assert.Equal(HttpStatusCode.BadRequest, tooShortResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/me")).StatusCode);

        using var acceptedResponse = await client.PostAsJsonAsync(
            "/v1/auth/password",
            new ChangePasswordRequest(account.Password, "12345678"));
        Assert.Equal(HttpStatusCode.NoContent, acceptedResponse.StatusCode);

        using var loginClient = CreateCookieClient(factory);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginResponseAsync(loginClient, account.Email, "12345678")).StatusCode);
    }

    [Fact]
    public async Task ServerValidationRejectsIdleExpiredAndSecurityStampMismatchedSessions()
    {
        var account = await CreateCustomerAsync("server-validation");
        using var factory = new BidMatrixApiFactory(database);

        using (var idleClient = CreateCookieClient(factory))
        {
            await LoginAsync(idleClient, account.Email, account.Password);
            await UpdateLatestSessionAsync(
                account.UserId,
                "created_at = now() - interval '32 minutes', last_seen_at = now() - interval '31 minutes'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await idleClient.GetAsync("/v1/me")).StatusCode);
        }

        using (var expiredClient = CreateCookieClient(factory))
        {
            await LoginAsync(expiredClient, account.Email, account.Password);
            await UpdateLatestSessionAsync(
                account.UserId,
                "absolute_expires_at = created_at + interval '1 millisecond'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await expiredClient.GetAsync("/v1/me")).StatusCode);
        }

        using (var mismatchedClient = CreateCookieClient(factory))
        {
            await LoginAsync(mismatchedClient, account.Email, account.Password);
            await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "update user_credentials set security_stamp = $1 where user_id = $2";
            command.Parameters.AddWithValue(Guid.CreateVersion7());
            command.Parameters.AddWithValue(account.UserId);
            await command.ExecuteNonQueryAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, (await mismatchedClient.GetAsync("/v1/me")).StatusCode);
        }

        await using var verificationConnection = await database.MigrationDataSource.OpenConnectionAsync();
        Assert.Equal(1, await CountAsync(
            verificationConnection,
            "select count(*) from user_sessions where user_id = $1 and revoked_reason = 'idle_timeout'",
            account.UserId));
        Assert.Equal(1, await CountAsync(
            verificationConnection,
            "select count(*) from user_sessions where user_id = $1 and revoked_reason = 'absolute_timeout'",
            account.UserId));
    }

    [Fact]
    public async Task RecoveryTokenIsHashOnlySingleUseAndRevokesOldSessions()
    {
        var account = await CreateCustomerAsync("recovery");
        const string newPassword = "recovered-customer-password-2026";
        using var factory = new BidMatrixApiFactory(database);
        using var ownerClient = CreateCookieClient(factory);
        using var customerClient = CreateCookieClient(factory);
        await LoginAsync(ownerClient, OwnerEmail, OwnerPassword);
        await LoginAsync(customerClient, account.Email, account.Password);

        await AddCsrfTokenAsync(ownerClient);
        using var createResponse = await ownerClient.PostAsJsonAsync(
            "/owner/v1/account-recovery-links",
            new CreateAccountRecoveryRequest(account.Email));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedAccountRecoveryResponse>();
        Assert.NotNull(created);
        Assert.Equal(64, created.Token.Length);
        Assert.Contains($"/recover#token={created.Token}", created.RecoveryUrl, StringComparison.Ordinal);

        using var listResponse = await ownerClient.GetAsync("/owner/v1/account-recovery-links");
        var listText = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Token, listText, StringComparison.Ordinal);
        Assert.DoesNotContain("tokenHash", listText, StringComparison.OrdinalIgnoreCase);

        using var firstResetClient = CreateCookieClient(factory);
        using var secondResetClient = CreateCookieClient(factory);
        await AddCsrfTokenAsync(firstResetClient);
        await AddCsrfTokenAsync(secondResetClient);
        var firstReset = firstResetClient.PostAsJsonAsync(
            "/v1/auth/recovery/reset",
            new ResetAccountPasswordRequest(created.Token, newPassword));
        var secondReset = secondResetClient.PostAsJsonAsync(
            "/v1/auth/recovery/reset",
            new ResetAccountPasswordRequest(created.Token, newPassword));
        var resetResponses = await Task.WhenAll(firstReset, secondReset);
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Gone],
            resetResponses.Select(response => response.StatusCode).Order().ToArray());
        foreach (var response in resetResponses)
        {
            response.Dispose();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await customerClient.GetAsync("/v1/me")).StatusCode);
        using var oldPasswordClient = CreateCookieClient(factory);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LoginResponseAsync(oldPasswordClient, account.Email, account.Password)).StatusCode);
        using var newPasswordClient = CreateCookieClient(factory);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginResponseAsync(newPasswordClient, account.Email, newPassword)).StatusCode);

        using var replayClient = CreateCookieClient(factory);
        await AddCsrfTokenAsync(replayClient);
        using var replayResponse = await replayClient.PostAsJsonAsync(
            "/v1/auth/recovery/reset",
            new ResetAccountPasswordRequest(created.Token, "another-password-value-2026"));
        Assert.Equal(HttpStatusCode.Gone, replayResponse.StatusCode);

        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var tokenCommand = connection.CreateCommand();
        tokenCommand.CommandText = "select token_hash from account_recovery_tokens where id = $1";
        tokenCommand.Parameters.AddWithValue(Guid.Parse(created.Recovery.Id));
        var storedHash = Assert.IsType<string>(await tokenCommand.ExecuteScalarAsync());
        Assert.Equal(64, storedHash.Length);
        Assert.NotEqual(created.Token, storedHash);
        Assert.Equal(0, await CountAsync(
            connection,
            "select count(*) from audit_events where metadata::text like '%' || $1 || '%'",
            created.Token));
    }

    [Fact]
    public async Task RevokedRecoveryIsUnavailableAndCustomersCannotIssueLinks()
    {
        var account = await CreateCustomerAsync("revoked-recovery");
        using var factory = new BidMatrixApiFactory(database);
        using var ownerClient = CreateCookieClient(factory);
        using var customerClient = CreateCookieClient(factory);
        await LoginAsync(ownerClient, OwnerEmail, OwnerPassword);
        await LoginAsync(customerClient, account.Email, account.Password);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await customerClient.GetAsync("/owner/v1/account-recovery-links")).StatusCode);

        await AddCsrfTokenAsync(ownerClient);
        using var createResponse = await ownerClient.PostAsJsonAsync(
            "/owner/v1/account-recovery-links",
            new CreateAccountRecoveryRequest(account.Email));
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedAccountRecoveryResponse>();
        Assert.NotNull(created);
        using var revokeResponse = await ownerClient.PostAsync(
            $"/owner/v1/account-recovery-links/{created.Recovery.Id}/revoke",
            null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        using var anonymousClient = CreateCookieClient(factory);
        await AddCsrfTokenAsync(anonymousClient);
        using var inspectResponse = await anonymousClient.PostAsJsonAsync(
            "/v1/auth/recovery/inspect",
            new InspectAccountRecoveryRequest(created.Token));
        Assert.Equal(HttpStatusCode.Gone, inspectResponse.StatusCode);

        using var expiredCreateResponse = await ownerClient.PostAsJsonAsync(
            "/owner/v1/account-recovery-links",
            new CreateAccountRecoveryRequest(account.Email));
        var expiredRecovery = await expiredCreateResponse.Content.ReadFromJsonAsync<CreatedAccountRecoveryResponse>();
        Assert.NotNull(expiredRecovery);
        await using (var connection = await database.MigrationDataSource.OpenConnectionAsync())
        await using (var expireCommand = connection.CreateCommand())
        {
            expireCommand.CommandText = """
                update account_recovery_tokens
                set expires_at = created_at + interval '1 millisecond'
                where id = $1
                """;
            expireCommand.Parameters.AddWithValue(Guid.Parse(expiredRecovery.Recovery.Id));
            await expireCommand.ExecuteNonQueryAsync();
        }

        using var expiredInspectResponse = await anonymousClient.PostAsJsonAsync(
            "/v1/auth/recovery/inspect",
            new InspectAccountRecoveryRequest(expiredRecovery.Token));
        Assert.Equal(HttpStatusCode.Gone, expiredInspectResponse.StatusCode);
    }

    [Fact]
    public async Task RecoveryIssuanceRequiresRecentPlatformOwnerAuthentication()
    {
        var account = await CreateCustomerAsync("recent-auth");
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        using var baseFactory = new BidMatrixApiFactory(database);
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(timeProvider);
        }));
        using var ownerClient = CreateCookieClient(factory);
        await LoginAsync(ownerClient, OwnerEmail, OwnerPassword);
        timeProvider.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync("/owner/v1/account-recovery-links")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync("/v1/me")).StatusCode);

        await AddCsrfTokenAsync(ownerClient);
        using var createResponse = await ownerClient.PostAsJsonAsync(
            "/owner/v1/account-recovery-links",
            new CreateAccountRecoveryRequest(account.Email));
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    [Fact]
    public async Task OneShotOwnerBootstrapRefusesAnExistingPlatformOwner()
    {
        using var factory = new BidMatrixApiFactory(database);
        _ = factory.CreateClient();
        var service = factory.Services.GetRequiredService<OwnerBootstrapService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BootstrapInitialOwnerAsync());

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneShotOwnerBootstrapCreatesExactlyOneOwnerOnAnEmptyDatabase()
    {
        var databaseName = $"bidmatrix_bootstrap_{Guid.NewGuid():N}";
        var serverOptions = CopyDatabaseOptions("postgres");
        var bootstrapOptions = CopyDatabaseOptions(databaseName);

        await using (var serverDataSource = NpgsqlDataSource.Create(serverOptions.BuildMigrationConnectionString()))
        await using (var connection = await serverDataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"create database {QuoteIdentifier(databaseName)}";
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await new DatabaseMigrator(bootstrapOptions).ApplyAsync();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OWNER_BOOTSTRAP_EMAIL"] = "initial-owner@example.invalid",
                    ["OWNER_BOOTSTRAP_PASSWORD"] = "Initial-owner-password-2026!",
                    ["OWNER_BOOTSTRAP_DISPLAY_NAME"] = "Initial Owner",
                    ["OWNER_BOOTSTRAP_ORGANIZATION_NAME"] = "BidMatrix Operations",
                    ["OWNER_BOOTSTRAP_ORGANIZATION_SLUG"] = "bidmatrix-operations",
                })
                .Build();
            var service = new OwnerBootstrapService(
                bootstrapOptions,
                configuration,
                new PasswordHasher<AuthenticationPasswordSubject>(),
                NullLogger<OwnerBootstrapService>.Instance);

            await service.BootstrapInitialOwnerAsync();

            await using var bootstrapDataSource = NpgsqlDataSource.Create(
                bootstrapOptions.BuildMigrationConnectionString());
            await using var bootstrapConnection = await bootstrapDataSource.OpenConnectionAsync();
            await using var countCommand = bootstrapConnection.CreateCommand();
            countCommand.CommandText = """
                select
                    (select count(*) from user_platform_roles where role = 'platform_owner'),
                    (select count(*) from organization_memberships where role = 'owner'),
                    (select count(*) from audit_events where action = 'identity.owner.bootstrapped')
                """;
            await using var reader = await countCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.Equal(1, reader.GetInt64(1));
            Assert.Equal(1, reader.GetInt64(2));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BootstrapInitialOwnerAsync());
            Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await using var cleanupDataSource = NpgsqlDataSource.Create(serverOptions.BuildMigrationConnectionString());
            await using var cleanupConnection = await cleanupDataSource.OpenConnectionAsync();
            await using var cleanupCommand = cleanupConnection.CreateCommand();
            cleanupCommand.CommandText = $"drop database if exists {QuoteIdentifier(databaseName)} with (force)";
            await cleanupCommand.ExecuteNonQueryAsync();
        }
    }

    private static HttpClient CreateCookieClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        using var response = await LoginResponseAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginResponseAsync(
        HttpClient client,
        string email,
        string password)
    {
        await AddCsrfTokenAsync(client);
        return await client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(email, password));
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

    private async Task<TestAccount> CreateCustomerAsync(string prefix)
    {
        var userId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();
        var email = $"{prefix}-{userId:N}@example.invalid";
        var password = $"{prefix}-customer-password-2026";
        var passwordHasher = new PasswordHasher<TestPasswordSubject>();
        var passwordHash = passwordHasher.HashPassword(new TestPasswordSubject(userId), password);

        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = """
                insert into users (id, email, normalized_email, display_name, status, created_at, updated_at)
                values ($1, $2, $3, 'Security Test Customer', 'active', now(), now())
                """;
            userCommand.Parameters.AddWithValue(userId);
            userCommand.Parameters.AddWithValue(email);
            userCommand.Parameters.AddWithValue(email.ToUpperInvariant());
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (var organizationCommand = connection.CreateCommand())
        {
            organizationCommand.Transaction = transaction;
            organizationCommand.CommandText = """
                insert into organizations (id, name, slug, status, created_at, updated_at)
                values ($1, 'Security Test Organization', $2, 'active', now(), now())
                """;
            organizationCommand.Parameters.AddWithValue(organizationId);
            organizationCommand.Parameters.AddWithValue($"{prefix}-{organizationId:N}");
            await organizationCommand.ExecuteNonQueryAsync();
        }

        await using (var membershipCommand = connection.CreateCommand())
        {
            membershipCommand.Transaction = transaction;
            membershipCommand.CommandText = """
                insert into organization_memberships (id, organization_id, user_id, role, created_at)
                values ($1, $2, $3, 'owner', now())
                """;
            membershipCommand.Parameters.AddWithValue(membershipId);
            membershipCommand.Parameters.AddWithValue(organizationId);
            membershipCommand.Parameters.AddWithValue(userId);
            await membershipCommand.ExecuteNonQueryAsync();
        }

        await using (var credentialCommand = connection.CreateCommand())
        {
            credentialCommand.Transaction = transaction;
            credentialCommand.CommandText = """
                insert into user_credentials (
                    user_id, password_hash, security_stamp, password_changed_at,
                    created_at, updated_at, version
                )
                values ($1, $2, $3, now(), now(), now(), 1)
                """;
            credentialCommand.Parameters.AddWithValue(userId);
            credentialCommand.Parameters.AddWithValue(passwordHash);
            credentialCommand.Parameters.AddWithValue(Guid.CreateVersion7());
            await credentialCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return new TestAccount(userId, email, password);
    }

    private static async Task<int> CountAsync(
        NpgsqlConnection connection,
        string sql,
        object parameter)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private BidMatrixDataSourceOptions CopyDatabaseOptions(string databaseName) => new()
    {
        Host = database.Options.Host,
        MigrationHost = database.Options.MigrationHost,
        Port = database.Options.Port,
        Database = databaseName,
        User = database.Options.User,
        Password = database.Options.Password,
        AuditUser = database.Options.AuditUser,
        AuditPassword = database.Options.AuditPassword,
        AuthUser = database.Options.AuthUser,
        AuthPassword = database.Options.AuthPassword,
        MigrationUser = database.Options.MigrationUser,
        MigrationPassword = database.Options.MigrationPassword,
        SslMode = database.Options.SslMode,
        ChannelBinding = database.Options.ChannelBinding,
    };

    private static string QuoteIdentifier(string identifier) =>
        new NpgsqlCommandBuilder().QuoteIdentifier(identifier);

    private async Task UpdateLatestSessionAsync(Guid userId, string assignment)
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            update user_sessions
            set {assignment}
            where id = (
                select id from user_sessions where user_id = $1 order by created_at desc limit 1
            )
            """;
        command.Parameters.AddWithValue(userId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed record TestAccount(Guid UserId, string Email, string Password);

    private sealed record TestPasswordSubject(Guid UserId);

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
