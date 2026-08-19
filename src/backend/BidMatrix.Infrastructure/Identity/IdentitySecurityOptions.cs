using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BidMatrix.Infrastructure.Identity;

public sealed record IdentitySecurityOptions(
    Uri PublicBaseUri,
    TimeSpan SessionIdleTimeout,
    TimeSpan SessionAbsoluteLifetime,
    TimeSpan RecoveryLifetime,
    TimeSpan RecentAuthenticationLifetime)
{
    private const int DefaultSessionIdleMinutes = 30;
    private const int DefaultSessionAbsoluteHours = 8;
    private const int DefaultRecoveryLifetimeMinutes = 30;
    private const int DefaultRecentAuthenticationMinutes = 15;

    public static IdentitySecurityOptions FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var publicBaseUri = ParsePublicBaseUri(configuration, environment);
        var sessionIdleMinutes = ParseBoundedInteger(
            configuration,
            "BIDMATRIX_SESSION_IDLE_MINUTES",
            DefaultSessionIdleMinutes,
            5,
            240);
        var sessionAbsoluteHours = ParseBoundedInteger(
            configuration,
            "BIDMATRIX_SESSION_ABSOLUTE_HOURS",
            DefaultSessionAbsoluteHours,
            1,
            24);
        var recoveryLifetimeMinutes = ParseBoundedInteger(
            configuration,
            "BIDMATRIX_RECOVERY_LIFETIME_MINUTES",
            DefaultRecoveryLifetimeMinutes,
            10,
            120);
        var recentAuthenticationMinutes = ParseBoundedInteger(
            configuration,
            "BIDMATRIX_RECENT_AUTH_MINUTES",
            DefaultRecentAuthenticationMinutes,
            5,
            60);

        return new IdentitySecurityOptions(
            publicBaseUri,
            TimeSpan.FromMinutes(sessionIdleMinutes),
            TimeSpan.FromHours(sessionAbsoluteHours),
            TimeSpan.FromMinutes(recoveryLifetimeMinutes),
            TimeSpan.FromMinutes(recentAuthenticationMinutes));
    }

    private static Uri ParsePublicBaseUri(IConfiguration configuration, IHostEnvironment environment)
    {
        var value = configuration["BIDMATRIX_PUBLIC_BASE_URL"];
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("BIDMATRIX_PUBLIC_BASE_URL is required outside Development.");
            }

            value = "http://localhost:3000";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "BIDMATRIX_PUBLIC_BASE_URL must be an absolute HTTP or HTTPS origin without credentials, path, query, or fragment.");
        }

        if (!environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("BIDMATRIX_PUBLIC_BASE_URL must use HTTPS outside Development.");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
    }

    private static int ParseBoundedInteger(
        IConfiguration configuration,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var value = configuration[key];
        var parsed = string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var configured)
                ? configured
                : throw new InvalidOperationException($"{key} must be an integer.");

        if (parsed < minimum || parsed > maximum)
        {
            throw new InvalidOperationException($"{key} must be between {minimum} and {maximum}.");
        }

        return parsed;
    }
}
