using Npgsql;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class DatabaseSecurityTests(DatabaseFixture database)
{
    [Fact]
    public async Task EmptyDatabaseReceivesCoreSchemaAndDevelopmentSeed()
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();

        Assert.Equal(8, await CountAsync(connection, "select count(*) from schema_migrations"));
        Assert.Equal(4, await CountAsync(connection, "select count(*) from agent_definitions"));
        Assert.Equal(4, await CountAsync(connection, "select count(*) from agent_versions"));
        Assert.Equal(6, await CountAsync(connection, "select count(*) from system_controls"));
        Assert.Equal(1, await CountAsync(
            connection,
            "select count(*) from user_platform_roles where role = 'platform_owner'"));
        Assert.Equal(18, await CountAsync(
            connection,
            "select count(*) from pg_class where relrowsecurity and relname in ('organizations','organization_memberships','analyses','analysis_files','analysis_pages','analysis_requirements','analysis_citations','company_profiles','evidence_items','requirement_evidence_matches','tasks','task_dependencies','artifacts','tool_calls','approvals','workflow_runs','agent_runs','engineering_sandboxes')"));
    }

    [Fact]
    public async Task ApplicationAndAuditRolesAreRestrictedAndDoNotOwnDatabase()
    {
        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select
                pg_get_userbyid(database.datdba) as owner_name,
                app.rolsuper as app_superuser,
                app.rolbypassrls as app_bypass_rls,
                audit.rolsuper as audit_superuser,
                audit.rolbypassrls as audit_bypass_rls
            from pg_database database
            join pg_roles app on app.rolname = $1
            join pg_roles audit on audit.rolname = $2
            where database.datname = current_database()
            """;
        command.Parameters.AddWithValue(database.Options.User);
        command.Parameters.AddWithValue(database.Options.AuditUser);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(database.Options.MigrationUser, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
        Assert.False(reader.GetBoolean(4));
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
