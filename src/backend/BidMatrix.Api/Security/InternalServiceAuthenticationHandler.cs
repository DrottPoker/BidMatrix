using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BidMatrix.Api.Security;

public sealed class InternalServiceAuthenticationOptions : AuthenticationSchemeOptions
{
    public string Token { get; set; } = string.Empty;
}

public sealed class InternalServiceAuthenticationHandler(
    IOptionsMonitor<InternalServiceAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<InternalServiceAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authorization = authorizationValues.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Unsupported authorization scheme."));
        }

        var suppliedToken = authorization[bearerPrefix.Length..].Trim();
        if (!TokenMatches(suppliedToken, Options.Token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid service credential."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "agent-worker"),
            new Claim(ClaimTypes.Name, "BidMatrix Agent Worker"),
            new Claim(ClaimTypes.Role, "internal_service"),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool TokenMatches(string suppliedToken, string configuredToken)
    {
        if (string.IsNullOrWhiteSpace(suppliedToken) || string.IsNullOrWhiteSpace(configuredToken))
        {
            return false;
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredToken);
        return suppliedBytes.Length == configuredBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, configuredBytes);
    }
}
