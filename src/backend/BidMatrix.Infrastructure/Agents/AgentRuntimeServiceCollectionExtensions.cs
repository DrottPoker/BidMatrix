using BidMatrix.Application.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace BidMatrix.Infrastructure.Agents;

public static class AgentRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddBidMatrixAgentRuntime(this IServiceCollection services)
    {
        services.AddScoped<IAgentRuntimeService, PostgresAgentRuntimeService>();
        return services;
    }
}
