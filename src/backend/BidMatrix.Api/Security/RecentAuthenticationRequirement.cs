using System.Globalization;
using System.Security.Claims;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace BidMatrix.Api.Security;

public sealed class RecentAuthenticationRequirement : IAuthorizationRequirement;

public sealed class RecentAuthenticationHandler(
    TimeProvider timeProvider,
    IdentitySecurityOptions options) : AuthorizationHandler<RecentAuthenticationRequirement>
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(1);

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RecentAuthenticationRequirement requirement)
    {
        var value = context.User.FindFirstValue(BidMatrixClaimTypes.AuthenticationTime);
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            var now = timeProvider.GetUtcNow();
            if (authenticatedAt <= now.Add(AllowedClockSkew) &&
                authenticatedAt.Add(options.RecentAuthenticationLifetime) > now)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
