using BidMatrix.Database.Schema;
using Npgsql;

namespace BidMatrix.Api.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly Dictionary<string, string?> previousEnvironment = new(StringComparer.Ordinal);
    private string? databaseName;

    public BidMatrixDataSourceOptions Options { get; private set; } = null!;
    public NpgsqlDataSource ApplicationDataSource { get; private set; } = null!;
    public NpgsqlDataSource AuditDataSource { get; private set; } = null!;
    public NpgsqlDataSource MigrationDataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        databaseName = $"bidmatrix_test_{Guid.NewGuid():N}";
        var serverOptions = CreateOptions("postgres");

        await using (var serverDataSource = NpgsqlDataSource.Create(serverOptions.BuildMigrationConnectionString()))
        await using (var connection = await serverDataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"create database {QuoteIdentifier(databaseName)}";
            await command.ExecuteNonQueryAsync();
        }

        Options = CreateOptions(databaseName);
        await new DatabaseMigrator(Options).ApplyAsync();
        await new DevelopmentDataSeeder(Options).ApplyAsync();
        ApplyApiEnvironment();

        ApplicationDataSource = NpgsqlDataSource.Create(Options.BuildApplicationConnectionString());
        AuditDataSource = NpgsqlDataSource.Create(Options.BuildAuditConnectionString());
        MigrationDataSource = NpgsqlDataSource.Create(Options.BuildMigrationConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (ApplicationDataSource is not null)
        {
            await ApplicationDataSource.DisposeAsync();
        }

        if (AuditDataSource is not null)
        {
            await AuditDataSource.DisposeAsync();
        }

        if (MigrationDataSource is not null)
        {
            await MigrationDataSource.DisposeAsync();
        }

        if (databaseName is null)
        {
            RestoreEnvironment();
            return;
        }

        var serverOptions = CreateOptions("postgres");
        await using var serverDataSource = NpgsqlDataSource.Create(serverOptions.BuildMigrationConnectionString());
        await using var connection = await serverDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"drop database if exists {QuoteIdentifier(databaseName)} with (force)";
        await command.ExecuteNonQueryAsync();
        RestoreEnvironment();
    }

    private static BidMatrixDataSourceOptions CreateOptions(string database) => new()
    {
        Host = GetEnvironmentValue("BIDMATRIX_TEST_POSTGRES_HOST", "localhost"),
        Port = int.Parse(GetEnvironmentValue(
            "BIDMATRIX_TEST_POSTGRES_PORT",
            GetEnvironmentValue("POSTGRES_EXPOSED_PORT", "55432"))),
        Database = database,
        User = GetEnvironmentValue("POSTGRES_APP_USER", "bidmatrix_app"),
        Password = GetEnvironmentValue("POSTGRES_APP_PASSWORD", "change-me-local-postgres-app"),
        AuditUser = GetEnvironmentValue("POSTGRES_AUDIT_USER", "bidmatrix_audit"),
        AuditPassword = GetEnvironmentValue("POSTGRES_AUDIT_PASSWORD", "change-me-local-postgres-audit"),
        MigrationUser = GetEnvironmentValue("POSTGRES_USER", "bidmatrix_admin"),
        MigrationPassword = GetEnvironmentValue("POSTGRES_PASSWORD", "change-me-local-postgres-admin"),
    };

    private static string GetEnvironmentValue(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static string QuoteIdentifier(string identifier) => new NpgsqlCommandBuilder().QuoteIdentifier(identifier);

    private void ApplyApiEnvironment()
    {
        SetEnvironmentValue("POSTGRES_HOST", Options.Host);
        SetEnvironmentValue("POSTGRES_PORT", Options.Port.ToString());
        SetEnvironmentValue("POSTGRES_DATABASE", Options.Database);
        SetEnvironmentValue("POSTGRES_APP_USER", Options.User);
        SetEnvironmentValue("POSTGRES_APP_PASSWORD", Options.Password);
        SetEnvironmentValue("POSTGRES_AUDIT_USER", Options.AuditUser);
        SetEnvironmentValue("POSTGRES_AUDIT_PASSWORD", Options.AuditPassword);
        SetEnvironmentValue("POSTGRES_USER", Options.MigrationUser);
        SetEnvironmentValue("POSTGRES_PASSWORD", Options.MigrationPassword);
        SetEnvironmentValue("OWNER_BOOTSTRAP_EMAIL", "owner@example.invalid");
        SetEnvironmentValue("OWNER_BOOTSTRAP_PASSWORD", "phase-three-owner-password");
        SetEnvironmentValue("OWNER_BOOTSTRAP_DISPLAY_NAME", "Integration Test Owner");
        SetEnvironmentValue("INTERNAL_SERVICE_TOKEN", "phase-three-internal-service-token");
        SetEnvironmentValue("BIDMATRIX_PUBLIC_BASE_URL", "http://localhost:3000");
        SetEnvironmentValue("S3_ENDPOINT", "http://localhost:9000");
        SetEnvironmentValue("S3_REGION", "us-east-1");
        SetEnvironmentValue("S3_BUCKET_QUARANTINE", "bidmatrix-test-quarantine");
        SetEnvironmentValue("S3_BUCKET_PRIVATE", "bidmatrix-test-private");
        SetEnvironmentValue("S3_ACCESS_KEY", "bidmatrix-test");
        SetEnvironmentValue("S3_SECRET_KEY", "bidmatrix-test-secret");
        SetEnvironmentValue("FILE_SCAN_MODE", "development_bypass");
        SetEnvironmentValue("MAX_PDF_UPLOAD_BYTES", "26214400");
        SetEnvironmentValue("FILE_RETENTION_DAYS", "30");
    }

    private void SetEnvironmentValue(string name, string value)
    {
        previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnvironment()
    {
        foreach (var (name, value) in previousEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        previousEnvironment.Clear();
    }
}
