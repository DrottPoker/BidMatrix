using System.Globalization;
using System.Security.Claims;
using BidMatrix.Application.Identity;

namespace BidMatrix.Infrastructure.Identity;

public sealed class ManagedOidcPrincipalFactory(
    TimeProvider timeProvider,
    IdentitySecurityOptions securityOptions)
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(1);

    public ClaimsPrincipal CreateExternalPrincipal(
        ClaimsPrincipal validatedPrincipal,
        string? validatedIssuer)
    {
        var issuer = ReadIssuer(validatedPrincipal, validatedIssuer);
        var subject = ReadRequiredClaim(validatedPrincipal, "sub", 1024);
        var email = ReadRequiredClaim(validatedPrincipal, "email", 320);
        var emailVerifiedValues = validatedPrincipal.FindAll("email_verified")
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (emailVerifiedValues is not [var emailVerified] ||
            !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidIdentity("The managed identity provider did not unambiguously verify the email address.");
        }

        var authenticationTime = ReadAuthenticationTime(
            ReadRequiredClaim(validatedPrincipal, "auth_time", 32));
        ValidateAuthenticationTime(authenticationTime);
        var displayName = ReadOptionalClaim(validatedPrincipal, "name", 120);
        var claims = new List<Claim>
        {
            new Claim(BidMatrixExternalClaimTypes.Issuer, issuer),
            new Claim(
                BidMatrixExternalClaimTypes.SubjectHash,
                FederatedSubjectUtility.Hash(issuer, subject)),
            new Claim(BidMatrixExternalClaimTypes.Email, email),
            new Claim(
                BidMatrixExternalClaimTypes.AuthenticationTime,
                authenticationTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        };
        if (displayName is not null)
        {
            claims.Add(new Claim(BidMatrixExternalClaimTypes.DisplayName, displayName));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            BidMatrixAuthenticationSchemes.ExternalCookie));
    }

    public FederatedIdentityProfile Create(ClaimsPrincipal externalPrincipal)
    {
        var issuer = ReadRequiredClaim(
            externalPrincipal,
            BidMatrixExternalClaimTypes.Issuer,
            2048);
        var subjectHash = ReadRequiredClaim(
            externalPrincipal,
            BidMatrixExternalClaimTypes.SubjectHash,
            64);
        var email = ReadRequiredClaim(
            externalPrincipal,
            BidMatrixExternalClaimTypes.Email,
            320);
        var authenticationTime = ReadAuthenticationTime(ReadRequiredClaim(
            externalPrincipal,
            BidMatrixExternalClaimTypes.AuthenticationTime,
            32));
        ValidateAuthenticationTime(authenticationTime);
        var displayName = ReadOptionalClaim(
            externalPrincipal,
            BidMatrixExternalClaimTypes.DisplayName,
            120);
        return new FederatedIdentityProfile(
            issuer,
            subjectHash,
            email,
            authenticationTime,
            displayName);
    }

    private void ValidateAuthenticationTime(DateTimeOffset authenticatedAt)
    {
        var now = timeProvider.GetUtcNow();
        if (authenticatedAt > now.Add(AllowedClockSkew) ||
            authenticatedAt.Add(securityOptions.RecentAuthenticationLifetime) <= now)
        {
            throw new FederatedIdentityException(
                "authentication_too_old",
                "The managed identity authentication is not recent enough.",
                401);
        }
    }

    private static DateTimeOffset ReadAuthenticationTime(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            throw InvalidIdentity("The managed identity authentication time is invalid.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidIdentity("The managed identity authentication time is invalid.");
        }
    }

    private static string ReadIssuer(ClaimsPrincipal principal, string? validatedIssuer)
    {
        var issuerValues = principal.FindAll("iss")
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var issuer = issuerValues switch
        {
            [var value] => value,
            [] when !string.IsNullOrWhiteSpace(validatedIssuer) => validatedIssuer,
            _ => throw InvalidIdentity("The managed identity issuer is missing or ambiguous."),
        };

        if (!string.IsNullOrWhiteSpace(validatedIssuer) &&
            !string.Equals(issuer, validatedIssuer, StringComparison.Ordinal))
        {
            throw InvalidIdentity("The managed identity issuer is inconsistent.");
        }

        if (issuer.Length > 2048 || issuer.Any(char.IsControl))
        {
            throw InvalidIdentity("The managed identity issuer is invalid.");
        }

        return issuer;
    }

    private static string ReadRequiredClaim(
        ClaimsPrincipal principal,
        string claimType,
        int maximumLength)
    {
        var values = principal.FindAll(claimType)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values is not [var value] ||
            string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(char.IsControl))
        {
            throw InvalidIdentity($"The managed identity claim '{claimType}' is missing or invalid.");
        }

        return value;
    }

    private static string? ReadOptionalClaim(
        ClaimsPrincipal principal,
        string claimType,
        int maximumLength)
    {
        var values = principal.FindAll(claimType)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        if (values is not [var value] ||
            string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(char.IsControl))
        {
            throw InvalidIdentity($"The managed identity claim '{claimType}' is invalid.");
        }

        return value.Trim();
    }

    private static FederatedIdentityException InvalidIdentity(string message) => new(
        "invalid_identity",
        message,
        401);
}

public static class BidMatrixExternalClaimTypes
{
    public const string Issuer = "bidmatrix:external_issuer";
    public const string SubjectHash = "bidmatrix:external_subject_hash";
    public const string Email = "bidmatrix:external_email";
    public const string AuthenticationTime = "bidmatrix:external_auth_time";
    public const string DisplayName = "bidmatrix:external_display_name";
}
