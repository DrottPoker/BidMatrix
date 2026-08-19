using System.Security.Claims;
using BidMatrix.Api.Security;
using BidMatrix.Application.Identity;
using BidMatrix.Contracts.Identity;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace BidMatrix.Api.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapBidMatrixIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/v1/auth").WithTags("Authentication");

        auth.MapGet("/csrf", GetCsrfToken)
            .AllowAnonymous()
            .WithName("GetCsrfToken");

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .RequireRateLimiting(BidMatrixSecurityServiceCollectionExtensions.LoginRateLimitPolicy)
            .WithName("Login");

        auth.MapPost("/logout", (Delegate)LogoutAsync)
            .RequireAuthorization(BidMatrixPolicies.AuthenticatedUser)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("Logout");

        endpoints.MapGet("/v1/me", GetCurrentUser)
            .RequireAuthorization(BidMatrixPolicies.AuthenticatedUser)
            .WithTags("Customer")
            .WithName("GetCurrentUser");

        endpoints.MapGet("/v1/organizations/current", GetCurrentOrganization)
            .RequireAuthorization(BidMatrixPolicies.Customer)
            .WithTags("Customer")
            .WithName("GetCurrentOrganization");

        endpoints.MapGet(
                "/internal/v1/status",
                () => Results.Ok(new InternalServiceStatusResponse("agent-worker", "authorized")))
            .RequireAuthorization(BidMatrixPolicies.InternalService)
            .WithTags("Internal")
            .WithName("GetInternalServiceStatus");

        return endpoints;
    }

    private static CsrfTokenResponse GetCsrfToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return new CsrfTokenResponse(
            tokens.RequestToken ?? throw new InvalidOperationException("Antiforgery request token was not generated."),
            tokens.HeaderName ?? "X-CSRF-TOKEN");
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        IUserAuthenticationService authenticationService,
        IUserSessionService sessionService,
        ManagedOidcOptions managedOidcOptions,
        HttpContext context,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!managedOidcOptions.NativeLoginEnabled)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "native_login_disabled",
                detail: "Native login is not enabled.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) ||
            request.Email.Length > 320 ||
            string.IsNullOrEmpty(request.Password) ||
            request.Password.Length > 256)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["A valid email and password are required."],
            });
        }

        var attempt = await authenticationService.AuthenticateAsync(
            request.Email,
            request.Password,
            cancellationToken);

        if (attempt.Status != AuthenticationStatus.Succeeded || attempt.User is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed",
                detail: "The email or password is invalid.");
        }

        var authenticatedAt = timeProvider.GetUtcNow();
        var session = await sessionService.CreateAsync(
            attempt.User.UserId,
            context.TraceIdentifier,
            cancellationToken);
        var principal = BidMatrixPrincipalFactory.Create(attempt.User, session, authenticatedAt);
        await context.SignInAsync(
            BidMatrixAuthenticationSchemes.Cookie,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false,
                IssuedUtc = authenticatedAt,
                ExpiresUtc = session.AbsoluteExpiresAt,
            });

        return Results.Ok(CreateCurrentUserResponse(principal));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IUserSessionService sessionService,
        CancellationToken cancellationToken)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionIdValue = context.User.FindFirstValue(BidMatrixClaimTypes.SessionId);
        if (!Guid.TryParse(userIdValue, out var userId) || !Guid.TryParse(sessionIdValue, out var sessionId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid session");
        }

        await sessionService.RevokeAsync(
            sessionId,
            userId,
            "logout",
            context.TraceIdentifier,
            cancellationToken);
        await context.SignOutAsync(BidMatrixAuthenticationSchemes.Cookie);
        return Results.Ok(new LogoutResponse(true));
    }

    private static IResult GetCurrentUser(ClaimsPrincipal principal) =>
        Results.Ok(CreateCurrentUserResponse(principal));

    private static IResult GetCurrentOrganization(ClaimsPrincipal principal)
    {
        var organizationId = principal.FindFirstValue(BidMatrixClaimTypes.OrganizationId);
        var organizationRole = principal.FindFirstValue(BidMatrixClaimTypes.OrganizationRole);

        return organizationId is null || organizationRole is null
            ? Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Organization context is unavailable")
            : Results.Ok(new CurrentOrganizationResponse(organizationId, organizationRole));
    }

    private static CurrentUserResponse CreateCurrentUserResponse(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user identifier.");
        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? throw new InvalidOperationException("Authenticated principal is missing an email.");
        var memberships = principal.FindAll(BidMatrixClaimTypes.Membership)
            .Select(ParseMembership)
            .Where(membership => membership is not null)
            .Cast<OrganizationMembershipResponse>()
            .ToArray();
        var platformRoles = principal.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(role => role.StartsWith("platform_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new CurrentUserResponse(
            userId,
            email,
            principal.FindFirstValue(ClaimTypes.Name),
            memberships,
            platformRoles);
    }

    private static OrganizationMembershipResponse? ParseMembership(Claim claim)
    {
        var separatorIndex = claim.Value.IndexOf('|', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == claim.Value.Length - 1)
        {
            return null;
        }

        var organizationId = claim.Value[..separatorIndex];
        return Guid.TryParse(organizationId, out _)
            ? new OrganizationMembershipResponse(organizationId, claim.Value[(separatorIndex + 1)..])
            : null;
    }
}
