using BidMatrix.Application.Analyses;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BidMatrix.Api.IntegrationTests;

public sealed class BidMatrixApiFactory(
    DatabaseFixture database,
    IReadOnlyDictionary<string, string?>? configurationOverrides = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["POSTGRES_HOST"] = database.Options.Host,
                ["POSTGRES_MIGRATION_HOST"] = database.Options.MigrationHost,
                ["POSTGRES_PORT"] = database.Options.Port.ToString(),
                ["POSTGRES_DATABASE"] = database.Options.Database,
                ["POSTGRES_SSL_MODE"] = database.Options.SslMode.ToString(),
                ["POSTGRES_CHANNEL_BINDING"] = database.Options.ChannelBinding.ToString(),
                ["POSTGRES_APP_USER"] = database.Options.User,
                ["POSTGRES_APP_PASSWORD"] = database.Options.Password,
                ["POSTGRES_AUDIT_USER"] = database.Options.AuditUser,
                ["POSTGRES_AUDIT_PASSWORD"] = database.Options.AuditPassword,
                ["POSTGRES_AUTH_USER"] = database.Options.AuthUser,
                ["POSTGRES_AUTH_PASSWORD"] = database.Options.AuthPassword,
                ["POSTGRES_USER"] = database.Options.MigrationUser,
                ["POSTGRES_PASSWORD"] = database.Options.MigrationPassword,
                ["OWNER_BOOTSTRAP_EMAIL"] = "owner@example.invalid",
                ["OWNER_BOOTSTRAP_PASSWORD"] = "phase-three-owner-password",
                ["OWNER_BOOTSTRAP_DISPLAY_NAME"] = "Integration Test Owner",
                ["OWNER_BOOTSTRAP_ORGANIZATION_NAME"] = "BidMatrix Integration Test Organization",
                ["OWNER_BOOTSTRAP_ORGANIZATION_SLUG"] = "bidmatrix-integration-test",
                ["INTERNAL_SERVICE_TOKEN"] = "phase-three-internal-service-token",
                ["BIDMATRIX_PUBLIC_BASE_URL"] = "http://localhost:3000",
                ["BIDMATRIX_SESSION_IDLE_MINUTES"] = "30",
                ["BIDMATRIX_SESSION_ABSOLUTE_HOURS"] = "8",
                ["BIDMATRIX_RECOVERY_LIFETIME_MINUTES"] = "30",
                ["BIDMATRIX_RECENT_AUTH_MINUTES"] = "15",
                ["BIDMATRIX_OIDC_ENABLED"] = "false",
                ["BIDMATRIX_NATIVE_LOGIN_ENABLED"] = "true",
                ["BIDMATRIX_NATIVE_RECOVERY_ENABLED"] = "true",
                ["BIDMATRIX_IDENTITY_TRANSITION_MODE"] = "false",
            };
            if (configurationOverrides is not null)
            {
                foreach (var (key, value) in configurationOverrides)
                {
                    values[key] = value;
                }
            }

            configuration.AddInMemoryCollection(values);
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<ManagedOidcOptions>();
            services.AddSingleton(provider => ManagedOidcOptions.FromConfiguration(
                provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<IHostEnvironment>()));

            services.RemoveAll<IObjectStorage>();
            services.AddSingleton<InMemoryObjectStorage>();
            services.AddSingleton<IObjectStorage>(provider => provider.GetRequiredService<InMemoryObjectStorage>());
        });
    }
}
