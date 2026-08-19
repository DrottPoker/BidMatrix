using Npgsql;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class DatabaseSecurityTests(DatabaseFixture database)
{
    [Fact]
    public async Task EmptyDatabaseReceivesCoreSchemaAndDevelopmentSeed()
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();

        Assert.Equal(17, await CountAsync(connection, "select count(*) from schema_migrations"));
        Assert.Equal(4, await CountAsync(connection, "select count(*) from agent_definitions"));
        Assert.Equal(4, await CountAsync(connection, "select count(*) from agent_versions"));
        Assert.Equal(6, await CountAsync(connection, "select count(*) from system_controls"));
        Assert.Equal(1, await CountAsync(
            connection,
            "select count(*) from user_platform_roles where role = 'platform_owner'"));
        Assert.Equal(22, await CountAsync(
            connection,
            "select count(*) from pg_class where relrowsecurity and relname in ('organizations','organization_memberships','analyses','analysis_files','analysis_pages','analysis_requirements','analysis_citations','analysis_findings','company_profiles','evidence_items','requirement_evidence_matches','tasks','task_dependencies','artifacts','tool_calls','approvals','workflow_runs','agent_runs','engineering_sandboxes','user_sessions','account_recovery_tokens','user_federated_identities')"));
    }

    [Fact]
    public async Task BetterAuthRoleIsIsolatedFromBidMatrixAuthorityAndRegistrationFunctions()
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select
                has_schema_privilege($1, 'better_auth', 'usage'),
                has_table_privilege($1, 'better_auth."user"', 'select'),
                has_table_privilege($1, 'better_auth."session"', 'insert'),
                has_table_privilege($1, 'better_auth."rateLimit"', 'update'),
                has_function_privilege(
                    $1,
                    'public.revoke_bidmatrix_sessions_after_managed_password_update()',
                    'execute'),
                has_table_privilege($1, 'users', 'select'),
                has_table_privilege($1, 'organization_memberships', 'select'),
                to_regprocedure('public.inspect_tenant_owner_invitation(text, timestamptz)') is null,
                has_function_privilege(
                    $1,
                    'register_federated_account(uuid, uuid, uuid, uuid, text, text, text, text, text, text, text, text, uuid, timestamptz, text)',
                    'execute')
            """;
        command.Parameters.AddWithValue(database.Options.AuthUser);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.False(reader.GetBoolean(4));
        Assert.False(reader.GetBoolean(5));
        Assert.False(reader.GetBoolean(6));
        Assert.True(reader.GetBoolean(7));
        Assert.False(reader.GetBoolean(8));
    }

    [Fact]
    public async Task ManagedPasswordUpdateRevokesMappedBidMatrixSessionsTransactionally()
    {
        var userId = Guid.CreateVersion7();
        var identityId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var authUserId = $"auth-{Guid.NewGuid():N}";
        var issuer = "https://auth.example.test/api/auth";
        var subjectHash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{issuer}\n{authUserId}")));
        var email = $"managed-reset-{userId:N}@example.invalid";

        await using (var connection = await database.MigrationDataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                with inserted_user as (
                    insert into users (
                        id, email, normalized_email, display_name, status, created_at, updated_at
                    )
                    values ($1, $2, upper($2), 'Managed Reset Test', 'active', now(), now())
                    returning id
                ), inserted_credential as (
                    insert into user_credentials (
                        user_id, password_hash, security_stamp, password_changed_at,
                        created_at, updated_at, version
                    )
                    select id, 'disabled-managed-password-hash', $3, now(), now(), now(), 1
                    from inserted_user
                    returning user_id
                ), inserted_identity as (
                    insert into user_federated_identities (
                        id, user_id, provider_name, issuer, subject_hash, email_at_link,
                        status, linked_at, updated_at, version
                    )
                    select $4, user_id, 'Better Auth', $5, $6, $2, 'active', now(), now(), 1
                    from inserted_credential
                    returning user_id
                ), inserted_session as (
                    insert into user_sessions (
                        id, user_id, token_hash, created_at, last_seen_at,
                        absolute_expires_at, version
                    )
                    select $7, user_id, $8, now(), now(), now() + interval '8 hours', 1
                    from inserted_identity
                    returning user_id
                ), inserted_auth_user as (
                    insert into better_auth."user" (
                        "id", "name", "email", "emailVerified", "createdAt", "updatedAt"
                    )
                    select $9, 'Managed Reset Test', $2, true, now(), now()
                    from inserted_session
                    returning "id"
                )
                insert into better_auth."account" (
                    "id", "issuer", "accountId", "providerId", "userId",
                    "password", "createdAt", "updatedAt"
                )
                select $10, 'credential', "id", 'credential', "id", 'old-hash', now(), now()
                from inserted_auth_user
                """;
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(email);
            command.Parameters.AddWithValue(Guid.CreateVersion7());
            command.Parameters.AddWithValue(identityId);
            command.Parameters.AddWithValue(issuer);
            command.Parameters.AddWithValue(subjectHash);
            command.Parameters.AddWithValue(sessionId);
            command.Parameters.AddWithValue(new string('a', 64));
            command.Parameters.AddWithValue(authUserId);
            command.Parameters.AddWithValue($"account-{Guid.NewGuid():N}");
            await command.ExecuteNonQueryAsync();
        }

        await using (var authDataSource = NpgsqlDataSource.Create(database.Options.BuildAuthConnectionString()))
        await using (var authConnection = await authDataSource.OpenConnectionAsync())
        await using (var update = authConnection.CreateCommand())
        {
            update.CommandText = """
                update better_auth."account"
                set "password" = 'new-hash', "updatedAt" = now()
                where "userId" = $1 and "providerId" = 'credential'
                """;
            update.Parameters.AddWithValue(authUserId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        await using var verificationConnection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var verification = verificationConnection.CreateCommand();
        verification.CommandText = """
            select
                session_record.revoked_reason,
                credential.version,
                audit.action,
                audit.metadata ->> 'revokedSessionCount'
            from user_sessions session_record
            join user_credentials credential on credential.user_id = session_record.user_id
            join audit_events audit
              on audit.action = 'identity.managed_password.reset'
             and audit.target_id = session_record.user_id::text
            where session_record.id = $1
            """;
        verification.Parameters.AddWithValue(sessionId);
        await using var reader = await verification.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("managed_password_reset", reader.GetString(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal("identity.managed_password.reset", reader.GetString(2));
        Assert.Equal("1", reader.GetString(3));
    }

    [Fact]
    public async Task ApplicationAuditAndAuthRolesAreRestrictedAndDoNotOwnDatabase()
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select
                pg_get_userbyid(database.datdba) as owner_name,
                app.rolsuper as app_superuser,
                app.rolbypassrls as app_bypass_rls,
                audit.rolsuper as audit_superuser,
                audit.rolbypassrls as audit_bypass_rls,
                auth.rolsuper as auth_superuser,
                auth.rolbypassrls as auth_bypass_rls,
                auth.rolinherit as auth_inherits
            from pg_database database
            join pg_roles app on app.rolname = $1
            join pg_roles audit on audit.rolname = $2
            join pg_roles auth on auth.rolname = $3
            where database.datname = current_database()
            """;
        command.Parameters.AddWithValue(database.Options.User);
        command.Parameters.AddWithValue(database.Options.AuditUser);
        command.Parameters.AddWithValue(database.Options.AuthUser);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(database.Options.MigrationUser, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
        Assert.False(reader.GetBoolean(4));
        Assert.False(reader.GetBoolean(5));
        Assert.False(reader.GetBoolean(6));
        Assert.False(reader.GetBoolean(7));
    }

    [Fact]
    public async Task ApplicationRoleCannotReadIdentitySecretTablesDirectly()
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select
                has_table_privilege($1, 'user_credentials', 'select'),
                has_table_privilege($1, 'user_sessions', 'select'),
                has_table_privilege($1, 'account_recovery_tokens', 'select'),
                has_table_privilege($1, 'user_federated_identities', 'select'),
                has_table_privilege($1, 'user_credentials', 'update'),
                has_table_privilege($1, 'user_sessions', 'update'),
                has_table_privilege($1, 'account_recovery_tokens', 'update'),
                has_table_privilege($1, 'user_federated_identities', 'update')
            """;
        command.Parameters.AddWithValue(database.Options.User);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for (var index = 0; index < reader.FieldCount; index++)
        {
            Assert.False(reader.GetBoolean(index));
        }
    }

    [Fact]
    public async Task RowLevelSecurityPreventsCrossTenantReadsAndWrites()
    {
        var firstOrganizationId = Guid.CreateVersion7();
        var secondOrganizationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await SeedTenantRowsAsync(firstOrganizationId, secondOrganizationId, userId);

        await using var connection = await database.ApplicationDataSource.OpenConnectionAsync();
        await SetOrganizationAsync(connection, firstOrganizationId);

        Assert.Equal(1, await CountAsync(connection, "select count(*) from organizations"));
        Assert.Equal(1, await CountAsync(connection, "select count(*) from analyses"));

        await using (var update = connection.CreateCommand())
        {
            update.CommandText = "update analyses set title = 'cross-tenant mutation' where organization_id = $1";
            update.Parameters.AddWithValue(secondOrganizationId);
            Assert.Equal(0, await update.ExecuteNonQueryAsync());
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            insert into analyses (
                id,
                organization_id,
                title,
                status,
                source_language,
                created_by_user_id,
                requires_human_review,
                created_at,
                updated_at,
                version
            )
            values ($1, $2, 'forbidden', 'draft', 'en', $3, true, now(), now(), 1)
            """;
        insert.Parameters.AddWithValue(Guid.CreateVersion7());
        insert.Parameters.AddWithValue(secondOrganizationId);
        insert.Parameters.AddWithValue(userId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task AuditWriterAppendsHashChainAndMutationIsDenied()
    {
        var firstEventId = await AppendAuditEventAsync("phase2.first");
        var secondEventId = await AppendAuditEventAsync("phase2.second");

        await using (var connection = await database.MigrationDataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select second.previous_hash = first.event_hash
                from audit_events first
                join audit_events second on second.id = $2
                where first.id = $1
                """;
            command.Parameters.AddWithValue(firstEventId);
            command.Parameters.AddWithValue(secondEventId);
            Assert.True((bool)(await command.ExecuteScalarAsync() ?? false));
        }

        await using (var connection = await database.ApplicationDataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "update audit_events set summary = 'forbidden' where id = $1";
            command.Parameters.AddWithValue(firstEventId);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }

        await using (var connection = await database.MigrationDataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "update audit_events set summary = 'forbidden' where id = $1";
            command.Parameters.AddWithValue(firstEventId);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, exception.SqlState);
        }
    }

    private async Task SeedTenantRowsAsync(Guid firstOrganizationId, Guid secondOrganizationId, Guid userId)
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using (var userCommand = connection.CreateCommand())
        {
            userCommand.CommandText = """
            insert into users (id, email, normalized_email, status, created_at, updated_at)
            values ($1, $2, $3, 'active', now(), now())
            """;
            userCommand.Parameters.AddWithValue(userId);
            userCommand.Parameters.AddWithValue($"tenant-{userId:N}@example.invalid");
            userCommand.Parameters.AddWithValue($"TENANT-{userId:N}@EXAMPLE.INVALID");
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (var organizationCommand = connection.CreateCommand())
        {
            organizationCommand.CommandText = """
            insert into organizations (id, name, slug, status, created_at, updated_at)
            values
                ($1, 'First tenant', $2, 'active', now(), now()),
                ($3, 'Second tenant', $4, 'active', now(), now())
            """;
            organizationCommand.Parameters.AddWithValue(firstOrganizationId);
            organizationCommand.Parameters.AddWithValue($"first-{firstOrganizationId:N}");
            organizationCommand.Parameters.AddWithValue(secondOrganizationId);
            organizationCommand.Parameters.AddWithValue($"second-{secondOrganizationId:N}");
            await organizationCommand.ExecuteNonQueryAsync();
        }

        await using (var analysisCommand = connection.CreateCommand())
        {
            analysisCommand.CommandText = """
            insert into analyses (
                id,
                organization_id,
                title,
                status,
                source_language,
                created_by_user_id,
                requires_human_review,
                created_at,
                updated_at,
                version
            )
            values
                ($1, $2, 'First analysis', 'draft', 'en', $3, true, now(), now(), 1),
                ($4, $5, 'Second analysis', 'draft', 'en', $3, true, now(), now(), 1)
            """;
            analysisCommand.Parameters.AddWithValue(Guid.CreateVersion7());
            analysisCommand.Parameters.AddWithValue(firstOrganizationId);
            analysisCommand.Parameters.AddWithValue(userId);
            analysisCommand.Parameters.AddWithValue(Guid.CreateVersion7());
            analysisCommand.Parameters.AddWithValue(secondOrganizationId);
            await analysisCommand.ExecuteNonQueryAsync();
        }
    }

    private async Task<Guid> AppendAuditEventAsync(string action)
    {
        var eventId = Guid.CreateVersion7();
        await using var connection = await database.AuditDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select id
            from append_audit_event(
                $1,
                'test',
                'database-security-tests',
                $2,
                'database',
                null,
                null,
                null,
                null,
                'Phase 2 audit test',
                '{}'::jsonb,
                now()
            )
            """;
        command.Parameters.AddWithValue(eventId);
        command.Parameters.AddWithValue(action);
        Assert.Equal(eventId, (Guid)(await command.ExecuteScalarAsync() ?? Guid.Empty));
        return eventId;
    }

    private static async Task SetOrganizationAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select set_config('app.organization_id', $1, false)";
        command.Parameters.AddWithValue(organizationId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
