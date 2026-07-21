using System.Threading.RateLimiting;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace BidMatrix.Api.Security;

public static class BidMatrixSecurityServiceCollectionExtensions
{
    public const string WebCorsPolicy = "bidmatrix-web";
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddBidMatrixApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = BidMatrixAuthenticationSchemes.Cookie;
                options.DefaultChallengeScheme = BidMatrixAuthenticationSchemes.Cookie;
            })
            .AddCookie(BidMatrixAuthenticationSchemes.Cookie, options =>
            {
                options.Cookie.Name = environment.IsDevelopment()
                    ? "bidmatrix.session"
                    : "__Host-bidmatrix.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.EventsType = typeof(BidMatrixCookieAuthenticationEvents);
            })
            .AddScheme<InternalServiceAuthenticationOptions, InternalServiceAuthenticationHandler>(
                BidMatrixAuthenticationSchemes.InternalService,
                options => options.Token = configuration["INTERNAL_SERVICE_TOKEN"] ?? string.Empty);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.Cookie)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(BidMatrixPolicies.AuthenticatedUser, policy => policy
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.Cookie)
                .RequireAuthenticatedUser())
            .AddPolicy(BidMatrixPolicies.Customer, policy => policy
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.Cookie)
                .RequireAuthenticatedUser()
                .RequireClaim(BidMatrixClaimTypes.OrganizationId))
            .AddPolicy(BidMatrixPolicies.PlatformOwner, policy => policy
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.Cookie)
                .RequireAuthenticatedUser()
                .RequireRole("platform_owner"))
            .AddPolicy(BidMatrixPolicies.InternalService, policy => policy
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.InternalService)
                .RequireAuthenticatedUser()
                .RequireRole("internal_service"));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });

        var publicBaseUrl = configuration["BIDMATRIX_PUBLIC_BASE_URL"] ?? "http://localhost:3000";
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicOrigin))
        {
            throw new InvalidOperationException("BIDMATRIX_PUBLIC_BASE_URL must be an absolute URL.");
        }

        services.AddCors(options => options.AddPolicy(WebCorsPolicy, policy => policy
            .WithOrigins(publicOrigin.GetLeftPart(UriPartial.Authority))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        return services;
    }
}
