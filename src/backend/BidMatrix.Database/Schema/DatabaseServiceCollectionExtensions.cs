using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BidMatrix.Database.Schema;

public static class DatabaseServiceCollectionExtensions
{
    public const string AuditDataSourceKey = "audit";

    public static IServiceCollection AddBidMatrixDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var options = CreateOptions(configuration);
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

    public static BidMatrixDataSourceOptions CreateOptions(IConfiguration configuration) => new()
    {
        Host = configuration["POSTGRES_HOST"] ?? "localhost",
        MigrationHost = configuration["POSTGRES_MIGRATION_HOST"] ?? string.Empty,
        Port = int.TryParse(configuration["POSTGRES_PORT"], out var port) ? port : 5432,
        Database = configuration["POSTGRES_DATABASE"] ?? "bidmatrix",
        User = configuration["POSTGRES_APP_USER"] ?? "bidmatrix_app",
        Password = configuration["POSTGRES_APP_PASSWORD"] ?? string.Empty,
        AuditUser = configuration["POSTGRES_AUDIT_USER"] ?? "bidmatrix_audit",
        AuditPassword = configuration["POSTGRES_AUDIT_PASSWORD"] ?? string.Empty,
        AuthUser = configuration["POSTGRES_AUTH_USER"] ?? "bidmatrix_auth",
        AuthPassword = configuration["POSTGRES_AUTH_PASSWORD"] ?? string.Empty,
        MigrationUser = configuration["POSTGRES_USER"] ?? "bidmatrix_admin",
        MigrationPassword = configuration["POSTGRES_PASSWORD"] ?? string.Empty,
        SslMode = ParseEnum(configuration, "POSTGRES_SSL_MODE", SslMode.Prefer),
        ChannelBinding = ParseEnum(configuration, "POSTGRES_CHANNEL_BINDING", ChannelBinding.Prefer),
    };

    private static TEnum ParseEnum<TEnum>(
        IConfiguration configuration,
        string key,
        TEnum fallback)
        where TEnum : struct, Enum
    {
        if (configuration[key] is not { Length: > 0 } value)
        {
            return fallback;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(typeof(TEnum), parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{key} has unsupported value '{value}'.");
    }
}
