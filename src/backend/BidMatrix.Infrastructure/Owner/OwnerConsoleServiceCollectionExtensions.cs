using BidMatrix.Application.Owner;
using Microsoft.Extensions.DependencyInjection;

namespace BidMatrix.Infrastructure.Owner;

public static class OwnerConsoleServiceCollectionExtensions
{
    public static IServiceCollection AddBidMatrixOwnerConsole(this IServiceCollection services)
    {
        services.AddScoped<IOwnerConsoleService, PostgresOwnerConsoleService>();
        return services;
    }
}
