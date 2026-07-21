using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace BidMatrix.Infrastructure.Identity;

public sealed class BidMatrixCookieAuthenticationEvents(NpgsqlDataSource dataSource) : CookieAuthenticationEvents
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

        if (!Guid.TryParse(userIdValue, out var userId) || !Guid.TryParse(securityStampValue, out var securityStamp))
        {
            await RejectAsync(context);
            return;
        }

        await using var command = dataSource.CreateCommand("""
            select credential.security_stamp, user_record.status
            from user_credentials credential
            join users user_record on user_record.id = credential.user_id
            where credential.user_id = $1
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(context.HttpContext.RequestAborted);

        if (!await reader.ReadAsync(context.HttpContext.RequestAborted) ||
            reader.GetGuid(0) != securityStamp ||
            !string.Equals(reader.GetString(1), "active", StringComparison.Ordinal))
        {
            await RejectAsync(context);
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
    public const string OrganizationId = "bidmatrix:organization_id";
    public const string OrganizationRole = "bidmatrix:organization_role";
    public const string Membership = "bidmatrix:membership";
    public const string AuthenticationTime = "auth_time";
}

public static class BidMatrixAuthenticationSchemes
{
    public const string Cookie = "BidMatrixCookie";
    public const string InternalService = "BidMatrixInternalService";
}
