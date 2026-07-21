using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BidMatrix.Database.Schema;

public static class DatabaseServiceCollectionExtensions
{
    public const string AuditDataSourceKey = "audit";

    public static IServiceCollection AddBidMatrixDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new BidMatrixDataSourceOptions
        {
            Host = configuration["POSTGRES_HOST"] ?? "localhost",
            Port = int.TryParse(configuration["POSTGRES_PORT"], out var port) ? port : 5432,
            Database = configuration["POSTGRES_DATABASE"] ?? "bidmatrix",
            User = configuration["POSTGRES_APP_USER"] ?? "bidmatrix_app",
            Password = configuration["POSTGRES_APP_PASSWORD"] ?? string.Empty,
            AuditUser = configuration["POSTGRES_AUDIT_USER"] ?? "bidmatrix_audit",
            AuditPassword = configuration["POSTGRES_AUDIT_PASSWORD"] ?? string.Empty,
            MigrationUser = configuration["POSTGRES_USER"] ?? "bidmatrix_admin",
            MigrationPassword = configuration["POSTGRES_PASSWORD"] ?? string.Empty,
        };

        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(_ => NpgsqlDataSource.Create(options.BuildApplicationConnectionString()));
        services.AddKeyedSingleton(
            typeof(NpgsqlDataSource),
            AuditDataSourceKey,
            (_, _) => NpgsqlDataSource.Create(options.BuildAuditConnectionString()));
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<DevelopmentDataSeeder>();
        services.AddHostedService<DatabaseMigrationHostedService>();
        services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgresql");

        return services;
    }
}
