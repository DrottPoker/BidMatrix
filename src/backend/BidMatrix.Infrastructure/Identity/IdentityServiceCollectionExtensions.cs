using BidMatrix.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BidMatrix.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddBidMatrixIdentity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<AuthenticationPasswordSubject>, PasswordHasher<AuthenticationPasswordSubject>>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<BidMatrixCookieAuthenticationEvents>();
        services.AddHostedService<DevelopmentOwnerBootstrapHostedService>();
        return services;
    }
}
