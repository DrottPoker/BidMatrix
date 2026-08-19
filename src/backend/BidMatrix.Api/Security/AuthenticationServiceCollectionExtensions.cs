using System.Threading.RateLimiting;
using BidMatrix.Application.Identity;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BidMatrix.Api.Security;

public static class BidMatrixSecurityServiceCollectionExtensions
{
    private const string OidcFailureCodeItem = "bidmatrix:oidc_failure_code";
    public const string WebCorsPolicy = "bidmatrix-web";
    public const string LoginRateLimitPolicy = "login";
    public const string RecoveryInspectionRateLimitPolicy = "recovery-inspection";
    public const string RecoveryResetRateLimitPolicy = "recovery-reset";

    public static IServiceCollection AddBidMatrixApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var identityOptions = IdentitySecurityOptions.FromConfiguration(configuration, environment);
        var managedOidcOptions = ManagedOidcOptions.FromConfiguration(configuration, environment);
        var authentication = services.AddAuthentication(options =>
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
                options.ExpireTimeSpan = identityOptions.SessionAbsoluteLifetime;
                options.SlidingExpiration = false;
                options.EventsType = typeof(BidMatrixCookieAuthenticationEvents);
            })
            .AddCookie(BidMatrixAuthenticationSchemes.ExternalCookie, options =>
            {
                options.Cookie.Name = environment.IsDevelopment()
                    ? "bidmatrix.external"
                    : "__Host-bidmatrix.external";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.SlidingExpiration = false;
            });

        if (managedOidcOptions.Enabled)
        {
            authentication.AddOpenIdConnect(BidMatrixAuthenticationSchemes.OpenIdConnect, options =>
            {
                options.SignInScheme = BidMatrixAuthenticationSchemes.ExternalCookie;
                options.Authority = managedOidcOptions.Authority!.AbsoluteUri;
                options.ClientId = managedOidcOptions.ClientId;
                options.ClientSecret = managedOidcOptions.ClientSecret;
                options.RequireHttpsMetadata = managedOidcOptions.RequireHttpsMetadata;
                options.CallbackPath = "/signin-oidc";
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.MapInboundClaims = false;
                options.MaxAge = identityOptions.RecentAuthenticationLifetime;
                options.UseTokenLifetime = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "roles",
                };
                options.Scope.Clear();
                options.Scope.Add(OpenIdConnectScope.OpenId);
                options.Scope.Add(OpenIdConnectScope.Profile);
                options.Scope.Add(OpenIdConnectScope.Email);
                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal is null)
                        {
                            context.Fail("Managed identity claims are invalid.");
                            return Task.CompletedTask;
                        }

                        try
                        {
                            var principalFactory = context.HttpContext.RequestServices
                                .GetRequiredService<ManagedOidcPrincipalFactory>();
                            context.Principal = principalFactory.CreateExternalPrincipal(
                                context.Principal,
                                context.SecurityToken.Issuer);
                        }
                        catch (FederatedIdentityException exception)
                        {
                            context.HttpContext.Items[OidcFailureCodeItem] = exception.Code;
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("BidMatrix.ManagedOidc");
                            logger.LogWarning(
                                "Managed OIDC claim validation failed with {FailureCode}",
                                exception.Code);
                            context.Fail("Managed identity claims are invalid.");
                        }

                        return Task.CompletedTask;
                    },
                    OnRedirectToIdentityProvider = context =>
                    {
                        context.ProtocolMessage.RedirectUri = new Uri(
                            managedOidcOptions.PublicApiBaseUri!,
                            options.CallbackPath.Value).AbsoluteUri;
                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("BidMatrix.ManagedOidc");
                        if (context.Failure is { } failure)
                        {
                            logger.LogWarning(
                                "Managed OIDC remote failure {ExceptionType}: {FailureMessage}",
                                failure.GetType().Name,
                                failure.Message);
                        }

                        context.HandleResponse();
                        var mode = context.Properties?.Items.TryGetValue(
                            ManagedOidcFlow.ModeKey,
                            out var flowMode) == true
                            ? flowMode
                            : null;
                        var failureCode = context.HttpContext.Items.TryGetValue(
                            OidcFailureCodeItem,
                            out var configuredFailureCode)
                            ? configuredFailureCode as string
                            : null;
                        var providerFailureCode = "provider";
                        if (environment.IsDevelopment() && context.Failure is { } providerFailure)
                        {
                            while (providerFailure.InnerException is { } innerFailure)
                            {
                                providerFailure = innerFailure;
                            }

                            providerFailureCode = $"provider_{providerFailure.GetType().Name}";
                        }
                        context.Response.Redirect(BuildPublicErrorRedirect(
                            identityOptions.PublicBaseUri,
                            failureCode ?? providerFailureCode,
                            mode));
                        return Task.CompletedTask;
                    },
                };

                if (environment.IsDevelopment())
                {
                    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.NonceCookie.SameSite = SameSiteMode.Lax;
                    options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                }
            });
        }

        authentication.AddScheme<InternalServiceAuthenticationOptions, InternalServiceAuthenticationHandler>(
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
            .AddPolicy(BidMatrixPolicies.RecentAuthenticatedUser, policy => policy
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.Cookie)
                .RequireAuthenticatedUser()
                .AddRequirements(new RecentAuthenticationRequirement()))
            .AddPolicy(BidMatrixPolicies.Customer, policy => policy
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.Cookie)
                .RequireAuthenticatedUser()
                .RequireClaim(BidMatrixClaimTypes.OrganizationId))
            .AddPolicy(BidMatrixPolicies.PlatformOwner, policy => policy
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.Cookie)
                .RequireAuthenticatedUser()
                .RequireRole("platform_owner"))
            .AddPolicy(BidMatrixPolicies.RecentPlatformOwner, policy => policy
                .AddAuthenticationSchemes(BidMatrixAuthenticationSchemes.Cookie)
                .RequireAuthenticatedUser()
                .RequireRole("platform_owner")
                .AddRequirements(new RecentAuthenticationRequirement()))
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
                        PermitLimit = environment.IsDevelopment() ? 20 : 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            options.AddPolicy(RecoveryInspectionRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            options.AddPolicy(RecoveryResetRateLimitPolicy, context =>
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

        services.AddSingleton<IAuthorizationHandler, RecentAuthenticationHandler>();

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

    private static string BuildPublicErrorRedirect(
        Uri publicBaseUri,
        string code,
        string? mode) =>
        QueryHelpers.AddQueryString(
            new Uri(publicBaseUri, ManagedOidcFlow.ErrorPath(mode)).AbsoluteUri,
            "identityError",
            code);
}
