using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BidMatrix.Infrastructure.Identity;

internal sealed class DevelopmentOwnerBootstrapHostedService(
    OwnerBootstrapService bootstrapService,
    IHostEnvironment environment,
    ILogger<DevelopmentOwnerBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var credentialChanged = await bootstrapService.SynchronizeDevelopmentOwnerAsync(cancellationToken);
        logger.LogInformation(
            credentialChanged
                ? "Development owner credential was synchronized from environment configuration."
                : "Development owner credential already matches environment configuration.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
