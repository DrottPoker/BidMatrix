using System.Security.Claims;
using BidMatrix.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace BidMatrix.Infrastructure.Identity;

public sealed class BidMatrixCookieAuthenticationEvents(
    IUserSessionService sessionService,
    IFederatedIdentityService federatedIdentityService,
    ManagedOidcOptions managedOidcOptions) : CookieAuthenticationEvents
{
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var securityStampValue = context.Principal?.FindFirstValue(BidMatrixClaimTypes.SecurityStamp);
        var sessionIdValue = context.Principal?.FindFirstValue(BidMatrixClaimTypes.SessionId);
        var sessionToken = context.Principal?.FindFirstValue(BidMatrixClaimTypes.SessionToken);
        var authenticationMethod = context.Principal?.FindFirstValue(BidMatrixClaimTypes.AuthenticationMethod);

        if (!Guid.TryParse(userIdValue, out var userId) ||
            !Guid.TryParse(securityStampValue, out var securityStamp) ||
            !Guid.TryParse(sessionIdValue, out var sessionId) ||
            string.IsNullOrEmpty(sessionToken) ||
            authenticationMethod is not (BidMatrixAuthenticationMethods.Password or BidMatrixAuthenticationMethods.Oidc))
        {
            await RejectAsync(context);
            return;
        }

        var validation = await sessionService.ValidateAsync(
            userId,
            sessionToken,
            securityStamp,
            context.HttpContext.RequestAborted);
        if (!validation.IsValid || validation.SessionId != sessionId)
        {
            await RejectAsync(context);
            return;
        }

        if (authenticationMethod == BidMatrixAuthenticationMethods.Password &&
            !managedOidcOptions.NativeLoginEnabled)
        {
            await RejectAsync(context);
            return;
        }

        if (authenticationMethod == BidMatrixAuthenticationMethods.Oidc)
        {
            var federatedIdentityIdValue = context.Principal?.FindFirstValue(BidMatrixClaimTypes.FederatedIdentityId);
            if (!Guid.TryParse(federatedIdentityIdValue, out var federatedIdentityId) ||
                !await federatedIdentityService.ValidateAsync(
                    federatedIdentityId,
                    userId,
                    context.HttpContext.RequestAborted))
            {
                await RejectAsync(context);
            }
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(BidMatrixAuthenticationSchemes.Cookie);
    }
}

public static class BidMatrixClaimTypes
{
    public const string SecurityStamp = "bidmatrix:security_stamp";
    public const string SessionId = "bidmatrix:session_id";
    public const string SessionToken = "bidmatrix:session_token";
    public const string OrganizationId = "bidmatrix:organization_id";
    public const string OrganizationRole = "bidmatrix:organization_role";
    public const string Membership = "bidmatrix:membership";
    public const string AuthenticationTime = "auth_time";
    public const string AuthenticationMethod = "bidmatrix:authentication_method";
    public const string FederatedIdentityId = "bidmatrix:federated_identity_id";
    public const string IdentityProvider = "bidmatrix:identity_provider";
}

public static class BidMatrixAuthenticationSchemes
{
    public const string Cookie = "BidMatrixCookie";
    public const string ExternalCookie = "BidMatrixExternalCookie";
    public const string OpenIdConnect = "BidMatrixOpenIdConnect";
    public const string InternalService = "BidMatrixInternalService";
}
