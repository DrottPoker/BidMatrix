using BidMatrix.Application.Analyses;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BidMatrix.Api.IntegrationTests;

public sealed class BidMatrixApiFactory(DatabaseFixture database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["POSTGRES_HOST"] = database.Options.Host,
                ["POSTGRES_PORT"] = database.Options.Port.ToString(),
                ["POSTGRES_DATABASE"] = database.Options.Database,
                ["POSTGRES_APP_USER"] = database.Options.User,
                ["POSTGRES_APP_PASSWORD"] = database.Options.Password,
                ["POSTGRES_AUDIT_USER"] = database.Options.AuditUser,
                ["POSTGRES_AUDIT_PASSWORD"] = database.Options.AuditPassword,
                ["POSTGRES_USER"] = database.Options.MigrationUser,
                ["POSTGRES_PASSWORD"] = database.Options.MigrationPassword,
                ["OWNER_BOOTSTRAP_EMAIL"] = "owner@example.invalid",
                ["OWNER_BOOTSTRAP_PASSWORD"] = "phase-three-owner-password",
                ["OWNER_BOOTSTRAP_DISPLAY_NAME"] = "Integration Test Owner",
                ["INTERNAL_SERVICE_TOKEN"] = "phase-three-internal-service-token",
                ["BIDMATRIX_PUBLIC_BASE_URL"] = "http://localhost:3000",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IObjectStorage>();
            services.AddSingleton<InMemoryObjectStorage>();
            services.AddSingleton<IObjectStorage>(provider => provider.GetRequiredService<InMemoryObjectStorage>());
        });
    }
}
