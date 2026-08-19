using System.Security.Claims;
using BidMatrix.Api.Security;
using BidMatrix.Application.Identity;
using BidMatrix.Contracts.Identity;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BidMatrix.Api.Endpoints;

public static class ManagedOidcEndpoints
{
    private const string CompletionPath = "/v1/auth/oidc/complete";

    public static IEndpointRouteBuilder MapBidMatrixManagedOidcEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/v1/auth").WithTags("Authentication");

        auth.MapGet("/configuration", GetConfiguration)
            .AllowAnonymous()
            .WithName("GetAuthenticationConfiguration");

        auth.MapGet("/oidc/login", StartLogin)
            .AllowAnonymous()
            .WithName("StartManagedOidcLogin");

        auth.MapGet("/oidc/link", StartLink)
            .RequireAuthorization(BidMatrixPolicies.RecentAuthenticatedUser)
            .WithName("StartManagedOidcLink");

        auth.MapGet("/oidc/complete", CompleteAsync)
            .AllowAnonymous()
            .WithName("CompleteManagedOidcAuthentication");

        auth.MapGet("/federated-identities", ListAsync)
            .RequireAuthorization(BidMatrixPolicies.AuthenticatedUser)
            .WithName("ListFederatedIdentities");

        auth.MapPost("/federated-identities/{identityId:guid}/revoke", RevokeAsync)
            .RequireAuthorization(BidMatrixPolicies.RecentAuthenticatedUser)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("RevokeFederatedIdentity");

        return endpoints;
    }

    private static AuthenticationConfigurationResponse GetConfiguration(ManagedOidcOptions options) => new(
        options.Enabled,
        options.ProviderName,
        options.NativeLoginEnabled,
        options.NativeRecoveryEnabled,
        options.TransitionMode);

    private static IResult StartLogin(
        [FromQuery] string? returnUrl,
        ManagedOidcOptions options)
    {
        if (!options.Enabled)
        {
            return ManagedIdentityUnavailable();
        }

        var properties = CreateChallengeProperties(ManagedOidcFlow.LoginMode, returnUrl, "/");
        return Results.Challenge(properties, [BidMatrixAuthenticationSchemes.OpenIdConnect]);
    }

    private static IResult StartLink(
        [FromQuery] string? returnUrl,
        ClaimsPrincipal principal,
        ManagedOidcOptions options)
    {
        if (!options.Enabled)
        {
            return ManagedIdentityUnavailable();
        }

        var properties = CreateChallengeProperties(ManagedOidcFlow.LinkMode, returnUrl, "/app/account");
        properties.Items[ManagedOidcFlow.UserIdKey] = ReadRequiredGuidClaim(
            principal,
            ClaimTypes.NameIdentifier).ToString();
        properties.Items[ManagedOidcFlow.SessionIdKey] = ReadRequiredGuidClaim(
            principal,
            BidMatrixClaimTypes.SessionId).ToString();
        properties.Items[ManagedOidcFlow.SecurityStampKey] = ReadRequiredGuidClaim(
            principal,
            BidMatrixClaimTypes.SecurityStamp).ToString();
        return Results.Challenge(properties, [BidMatrixAuthenticationSchemes.OpenIdConnect]);
    }

    private static async Task<IResult> CompleteAsync(
        HttpContext context,
        ManagedOidcOptions options,
        ManagedOidcPrincipalFactory principalFactory,
        IFederatedIdentityService federatedIdentityService,
        IUserSessionService sessionService,
        TimeProvider timeProvider,
        IdentitySecurityOptions identityOptions,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return ManagedIdentityUnavailable();
        }

        var externalAuthentication = await context.AuthenticateAsync(
            BidMatrixAuthenticationSchemes.ExternalCookie);
        await context.SignOutAsync(BidMatrixAuthenticationSchemes.ExternalCookie);

        var properties = externalAuthentication.Properties;
        var mode = ReadItem(properties, ManagedOidcFlow.ModeKey);
        var returnUrl = NormalizeReturnUrl(
            ReadItem(properties, ManagedOidcFlow.ReturnUrlKey),
            ManagedOidcFlow.DefaultReturnUrl(mode));
        if (!externalAuthentication.Succeeded || externalAuthentication.Principal is null || properties is null)
        {
            return ErrorRedirect(identityOptions.PublicBaseUri, mode, "external_session_invalid");
        }

        try
        {
            var profile = principalFactory.Create(externalAuthentication.Principal);
            AuthenticatedFederatedIdentity authentication;
            Guid? previousSessionId = null;

            if (mode == ManagedOidcFlow.LinkMode)
            {
                var userId = ReadRequiredGuidItem(properties, ManagedOidcFlow.UserIdKey);
                previousSessionId = ReadRequiredGuidItem(
                    properties,
                    ManagedOidcFlow.SessionIdKey);
                var securityStamp = ReadRequiredGuidItem(
                    properties,
                    ManagedOidcFlow.SecurityStampKey);
                authentication = await federatedIdentityService.LinkAsync(
                    new LinkFederatedIdentityCommand(
                        userId,
                        previousSessionId.Value,
                        securityStamp,
                        options.ProviderName,
                        profile,
                        context.TraceIdentifier),
                    cancellationToken);
            }
            else if (mode == ManagedOidcFlow.LoginMode)
            {
                try
                {
                    authentication = await federatedIdentityService.AuthenticateAsync(
                        profile,
                        context.TraceIdentifier,
                        cancellationToken);
                }
                catch (FederatedIdentityException exception) when (
                    exception.Code == "identity_not_linked")
                {
                    authentication = await federatedIdentityService.RegisterAsync(
                        new RegisterFederatedAccountCommand(
                            options.ProviderName,
                            profile,
                            context.TraceIdentifier),
                        cancellationToken);
                }
            }
            else
            {
                return ErrorRedirect(identityOptions.PublicBaseUri, mode, "flow_invalid");
            }

            var issuedAt = timeProvider.GetUtcNow();
            var session = await sessionService.CreateAsync(
                authentication.User.UserId,
                context.TraceIdentifier,
                cancellationToken);

            if (previousSessionId is { } sessionId)
            {
                await sessionService.RevokeAsync(
                    sessionId,
                    authentication.User.UserId,
                    "federated_identity_linked",
                    context.TraceIdentifier,
                    cancellationToken);
            }

            var bidMatrixPrincipal = BidMatrixPrincipalFactory.Create(
                authentication.User,
                session,
                issuedAt,
                new BidMatrixAuthenticationContext(
                    BidMatrixAuthenticationMethods.Oidc,
                    authentication.IdentityId,
                    options.ProviderName));
            await context.SignInAsync(
                BidMatrixAuthenticationSchemes.Cookie,
                bidMatrixPrincipal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = false,
                    IssuedUtc = issuedAt,
                    ExpiresUtc = session.AbsoluteExpiresAt,
                });

            return Results.Redirect(BuildPublicUri(identityOptions.PublicBaseUri, returnUrl));
        }
        catch (FederatedIdentityException exception)
        {
            return ErrorRedirect(identityOptions.PublicBaseUri, mode, exception.Code);
        }
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        IFederatedIdentityService service,
        CancellationToken cancellationToken)
    {
        var userId = ReadRequiredGuidClaim(principal, ClaimTypes.NameIdentifier);
        var identities = await service.ListAsync(userId, cancellationToken);
        var nativePasswordEnabled = await service.HasNativePasswordAsync(userId, cancellationToken);
        return Results.Ok(new FederatedIdentityListResponse(
            identities.Select(MapIdentity).ToArray(),
            nativePasswordEnabled));
    }

    private static async Task<IResult> RevokeAsync(
        Guid identityId,
        HttpContext context,
        IFederatedIdentityService service,
        ManagedOidcOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = ReadRequiredGuidClaim(context.User, ClaimTypes.NameIdentifier);
            var revoked = await service.RevokeAsync(
                new RevokeFederatedIdentityCommand(
                    identityId,
                    userId,
                    options.NativeLoginEnabled,
                    context.TraceIdentifier),
                cancellationToken);
            await context.SignOutAsync(BidMatrixAuthenticationSchemes.Cookie);
            return Results.Ok(new RevokeFederatedIdentityResponse(
                MapIdentity(revoked.Identity),
                revoked.RevokedSessionCount));
        }
        catch (FederatedIdentityException exception)
        {
            return Results.Problem(
                statusCode: exception.StatusCode,
                title: exception.Code,
                detail: exception.Message);
        }
    }

    private static AuthenticationProperties CreateChallengeProperties(
        string mode,
        string? returnUrl,
        string defaultReturnUrl)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = CompletionPath,
        };
        properties.Items[ManagedOidcFlow.ModeKey] = mode;
        properties.Items[ManagedOidcFlow.ReturnUrlKey] = NormalizeReturnUrl(
            returnUrl,
            defaultReturnUrl);
        return properties;
    }

    private static string NormalizeReturnUrl(string? returnUrl, string defaultReturnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return defaultReturnUrl;
        }

        return returnUrl.StartsWith("/", StringComparison.Ordinal) &&
               !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
               !returnUrl.Contains('\\') &&
               !returnUrl.Any(char.IsControl) &&
               Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            ? returnUrl
            : defaultReturnUrl;
    }

    private static string? ReadItem(AuthenticationProperties? properties, string key) =>
        properties?.Items.TryGetValue(key, out var value) == true ? value : null;

    private static Guid ReadRequiredGuidItem(AuthenticationProperties properties, string key) =>
        Guid.TryParse(ReadItem(properties, key), out var value)
            ? value
            : throw new FederatedIdentityException("flow_invalid", "The managed identity flow is invalid.", 401);

    private static Guid ReadRequiredGuidClaim(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new FederatedIdentityException("session_invalid", "The current session is invalid.", 401);

    private static FederatedIdentityResponse MapIdentity(FederatedIdentityRecord identity) => new(
        identity.Id.ToString(),
        identity.ProviderName,
        identity.Issuer,
        identity.EmailAtLink,
        identity.Status,
        identity.LinkedAt,
        identity.LastAuthenticatedAt,
        identity.RevokedAt,
        identity.Version);

    private static IResult ManagedIdentityUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "managed_identity_disabled",
        detail: "Managed identity is not enabled.");

    private static IResult ErrorRedirect(Uri publicBaseUri, string? mode, string code)
    {
        var uri = QueryHelpers.AddQueryString(
            new Uri(publicBaseUri, ManagedOidcFlow.ErrorPath(mode)).AbsoluteUri,
            "identityError",
            code);
        return Results.Redirect(uri);
    }

    private static string BuildPublicUri(Uri publicBaseUri, string relativePath) =>
        new Uri(publicBaseUri, relativePath).AbsoluteUri;
}
