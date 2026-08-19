using System.Security.Claims;
using BidMatrix.Application.Identity;

namespace BidMatrix.Infrastructure.Identity;

public static class BidMatrixPrincipalFactory
{
    public static ClaimsPrincipal Create(
        AuthenticatedUser user,
        CreatedUserSession session,
        DateTimeOffset authenticatedAt,
        BidMatrixAuthenticationContext? authenticationContext = null)
    {
        authenticationContext ??= BidMatrixAuthenticationContext.Password;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email),
            new(BidMatrixClaimTypes.SecurityStamp, user.SecurityStamp.ToString()),
            new(BidMatrixClaimTypes.SessionId, session.Id.ToString()),
            new(BidMatrixClaimTypes.SessionToken, session.Token),
            new(BidMatrixClaimTypes.AuthenticationTime, authenticatedAt.ToUnixTimeSeconds().ToString()),
            new(BidMatrixClaimTypes.AuthenticationMethod, authenticationContext.Method),
        };

        if (authenticationContext.FederatedIdentityId is { } identityId)
        {
            claims.Add(new Claim(BidMatrixClaimTypes.FederatedIdentityId, identityId.ToString()));
        }

        if (!string.IsNullOrEmpty(authenticationContext.ProviderName))
        {
            claims.Add(new Claim(BidMatrixClaimTypes.IdentityProvider, authenticationContext.ProviderName));
        }

        foreach (var membership in user.Memberships)
        {
            claims.Add(new Claim(
                BidMatrixClaimTypes.Membership,
                $"{membership.OrganizationId}|{membership.Role}"));
        }

        if (user.Memberships.FirstOrDefault() is { } currentMembership)
        {
            claims.Add(new Claim(BidMatrixClaimTypes.OrganizationId, currentMembership.OrganizationId.ToString()));
            claims.Add(new Claim(BidMatrixClaimTypes.OrganizationRole, currentMembership.Role));
        }

        claims.AddRange(user.PlatformRoles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, BidMatrixAuthenticationSchemes.Cookie));
    }
}

public sealed record BidMatrixAuthenticationContext(
    string Method,
    Guid? FederatedIdentityId,
    string? ProviderName)
{
    public static BidMatrixAuthenticationContext Password { get; } = new(
        BidMatrixAuthenticationMethods.Password,
        null,
        null);
}

public static class BidMatrixAuthenticationMethods
{
    public const string Password = "password";
    public const string Oidc = "oidc";
}
