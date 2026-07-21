using BidMatrix.Application.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace BidMatrix.Infrastructure.Internal;

public static class InternalFoundationServiceCollectionExtensions
{
    public static IServiceCollection AddBidMatrixInternalFoundation(this IServiceCollection services)
    {
        services.AddScoped<IInternalFoundationService, PostgresInternalFoundationService>();
        return services;
    }
}
