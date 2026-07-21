using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BidMatrix.Database.Schema;

public sealed class DatabaseMigrationHostedService(
    DatabaseMigrator migrator,
    DevelopmentDataSeeder seeder,
    IHostEnvironment environment,
    ILogger<DatabaseMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            logger.LogInformation("Skipping automatic database migrations outside Development.");
            return;
        }

        logger.LogInformation("Applying BidMatrix database migrations and Development seed data.");
        await migrator.ApplyAsync(cancellationToken);
        await seeder.ApplyAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
