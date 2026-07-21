using BidMatrix.Application.Tools;
using BidMatrix.Application.Engineering;
using BidMatrix.Infrastructure.Engineering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BidMatrix.Infrastructure.Tools;

public static class ToolGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddBidMatrixToolGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var sandboxOptions = new EngineeringSandboxOptions
        {
            BaseRepositoryPath = configuration["ENGINEERING_REPOSITORY_ROOT"] ?? string.Empty,
            WorktreeRootPath = configuration["ENGINEERING_WORKTREE_ROOT"] ?? Path.Combine(Path.GetTempPath(), "bidmatrix-engineering"),
            CommandTimeoutSeconds = int.TryParse(configuration["ENGINEERING_COMMAND_TIMEOUT_SECONDS"], out var timeout) ? timeout : 60,
            MaximumOutputBytes = int.TryParse(configuration["ENGINEERING_MAX_OUTPUT_BYTES"], out var outputBytes) ? outputBytes : 65_536,
        };
        services.AddSingleton(sandboxOptions);
        services.AddSingleton<IEngineeringSandboxService, EngineeringSandboxService>();
        services.AddSingleton<IPolicyEngine, DeterministicPolicyEngine>();
        services.AddScoped<IToolGatewayService, PostgresToolGatewayService>();
        services.AddScoped<IApprovalService, PostgresApprovalService>();
        return services;
    }
}
