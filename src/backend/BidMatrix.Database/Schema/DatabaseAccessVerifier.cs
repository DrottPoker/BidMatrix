using Npgsql;

namespace BidMatrix.Database.Schema;

public sealed class DatabaseAccessVerifier(BidMatrixDataSourceOptions options)
{
    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();

        await VerifyRoleAsync(
            options.BuildApplicationConnectionString(),
            options.User,
            cancellationToken);
        await VerifyRoleAsync(
            options.BuildAuditConnectionString(),
            options.AuditUser,
            cancellationToken);
        await VerifyRoleAsync(
            options.BuildAuthConnectionString(),
            options.AuthUser,
            cancellationToken);
    }

    private static async Task VerifyRoleAsync(
        string connectionString,
        string expectedRole,
        CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select
                current_user,
                current_database(),
                role_record.rolcanlogin,
                role_record.rolsuper,
                role_record.rolcreatedb,
                role_record.rolcreaterole,
                role_record.rolinherit,
                role_record.rolbypassrls,
                has_database_privilege(current_user, current_database(), 'connect'),
                has_schema_privilege(current_user, 'public', 'usage')
            from pg_roles role_record
            where role_record.rolname = current_user
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"PostgreSQL role {expectedRole} was not found.");
        }

        var actualRole = reader.GetString(0);
        var database = reader.GetString(1);
        var canLogin = reader.GetBoolean(2);
        var isSuperuser = reader.GetBoolean(3);
        var canCreateDatabase = reader.GetBoolean(4);
        var canCreateRole = reader.GetBoolean(5);
        var inheritsRoles = reader.GetBoolean(6);
        var bypassesRowLevelSecurity = reader.GetBoolean(7);
        var canConnect = reader.GetBoolean(8);
        var hasSchemaUsage = reader.GetBoolean(9);

        if (!string.Equals(actualRole, expectedRole, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(database) ||
            !canLogin ||
            isSuperuser ||
            canCreateDatabase ||
            canCreateRole ||
            inheritsRoles ||
            bypassesRowLevelSecurity ||
            !canConnect ||
            !hasSchemaUsage)
        {
            throw new InvalidOperationException(
                $"PostgreSQL role {expectedRole} does not satisfy the BidMatrix least-privilege boundary.");
        }
    }
}
