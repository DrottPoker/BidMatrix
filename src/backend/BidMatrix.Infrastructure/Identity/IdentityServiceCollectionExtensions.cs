using BidMatrix.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BidMatrix.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddBidMatrixIdentity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var securityOptions = IdentitySecurityOptions.FromConfiguration(configuration, environment);
        var managedOidcOptions = ManagedOidcOptions.FromConfiguration(configuration, environment);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(securityOptions);
        services.AddSingleton(managedOidcOptions);
        services.AddSingleton<IPasswordHasher<AuthenticationPasswordSubject>, PasswordHasher<AuthenticationPasswordSubject>>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IUserSessionService, PostgresUserSessionService>();
        services.AddScoped<IAccountSecurityService, PostgresAccountSecurityService>();
        services.AddScoped<IFederatedIdentityService, PostgresFederatedIdentityService>();
        services.AddScoped<ManagedOidcPrincipalFactory>();
        services.AddScoped<BidMatrixCookieAuthenticationEvents>();
        services.AddSingleton<OwnerBootstrapService>();
        services.AddHostedService<DevelopmentOwnerBootstrapHostedService>();
        return services;
    }
}
