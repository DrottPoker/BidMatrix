using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace BidMatrix.Database.Schema;

public sealed class DatabaseMigrator(BidMatrixDataSourceOptions options)
{
    private const string MigrationResourcePrefix = "BidMatrix.Database.Migrations.";

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();

        await using var migrationDataSource = NpgsqlDataSource.Create(options.BuildMigrationConnectionString());
        await using var connection = await migrationDataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await AcquireMigrationLockAsync(connection, transaction, cancellationToken);
        await EnsureMigrationHistoryAsync(connection, transaction, cancellationToken);

        foreach (var resourceName in GetMigrationResources())
        {
            var version = resourceName[MigrationResourcePrefix.Length..^4];
            var sql = await ReadResourceAsync(resourceName, cancellationToken);
            var renderedSql = RenderRoleNames(sql);
            var checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(renderedSql)));

            if (await MigrationAppliedAsync(connection, transaction, version, checksum, cancellationToken))
            {
                continue;
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = renderedSql;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "insert into schema_migrations (version, checksum) values ($1, $2)";
                command.Parameters.AddWithValue(version);
                command.Parameters.AddWithValue(checksum);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task AcquireMigrationLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(49089055130001)";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureMigrationHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            create table if not exists schema_migrations (
                version text primary key,
                checksum text null,
                applied_at timestamptz not null default now()
            );

            alter table schema_migrations add column if not exists checksum text null;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IEnumerable<string> GetMigrationResources() => Assembly.GetExecutingAssembly()
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(MigrationResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal);

    private static async Task<string> ReadResourceAsync(string resourceName, CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing migration resource {resourceName}.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private string RenderRoleNames(string sql)
    {
        var commandBuilder = new NpgsqlCommandBuilder();
        var appRole = commandBuilder.QuoteIdentifier(options.User);
        var auditRole = commandBuilder.QuoteIdentifier(options.AuditUser);

        return sql
            .Replace("{{APP_ROLE}}", appRole, StringComparison.Ordinal)
            .Replace("{{AUDIT_ROLE}}", auditRole, StringComparison.Ordinal);
    }

    private static async Task<bool> MigrationAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string version,
        string checksum,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select checksum from schema_migrations where version = $1";
        command.Parameters.AddWithValue(version);
        var storedChecksum = await command.ExecuteScalarAsync(cancellationToken);

        if (storedChecksum is null)
        {
            return false;
        }

        if (storedChecksum is DBNull)
        {
            await using var legacyCommand = connection.CreateCommand();
            legacyCommand.Transaction = transaction;
            legacyCommand.CommandText = "update schema_migrations set checksum = $2 where version = $1 and checksum is null";
            legacyCommand.Parameters.AddWithValue(version);
            legacyCommand.Parameters.AddWithValue(checksum);
            await legacyCommand.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        if (!string.Equals((string)storedChecksum, checksum, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Migration {version} has changed after it was applied.");
        }

        return true;
    }
}
