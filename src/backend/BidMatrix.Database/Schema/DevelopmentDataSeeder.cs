using System.Reflection;
using Npgsql;

namespace BidMatrix.Database.Schema;

public sealed class DevelopmentDataSeeder(BidMatrixDataSourceOptions options)
{
    private const string SeedResourcePrefix = "BidMatrix.Database.Seeds.";

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();

        await using var dataSource = NpgsqlDataSource.Create(options.BuildMigrationConnectionString());
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var resourceName in GetSeedResources())
        {
            await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing Development seed resource {resourceName}.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static IEnumerable<string> GetSeedResources() => Assembly.GetExecutingAssembly()
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(SeedResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal);
}
