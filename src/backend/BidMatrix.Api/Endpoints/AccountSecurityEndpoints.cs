using System.Security.Claims;
using BidMatrix.Api.Security;
using BidMatrix.Application.Identity;
using BidMatrix.Contracts.Identity;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace BidMatrix.Api.Endpoints;

public static class AccountSecurityEndpoints
{
    public static IEndpointRouteBuilder MapBidMatrixAccountSecurityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/v1/auth").WithTags("Authentication");

        auth.MapGet("/sessions", ListSessionsAsync)
            .RequireAuthorization(BidMatrixPolicies.AuthenticatedUser)
            .WithName("ListUserSessions");

        auth.MapPost("/sessions/{sessionId:guid}/revoke", RevokeSessionAsync)
            .RequireAuthorization(BidMatrixPolicies.AuthenticatedUser)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("RevokeUserSession");

        auth.MapPost("/sessions/revoke-others", RevokeOtherSessionsAsync)
            .RequireAuthorization(BidMatrixPolicies.AuthenticatedUser)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("RevokeOtherUserSessions");

        auth.MapPost("/password", ChangePasswordAsync)
            .RequireAuthorization(BidMatrixPolicies.AuthenticatedUser)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("ChangePassword");

        auth.MapPost("/recovery/inspect", InspectRecoveryAsync)
            .AllowAnonymous()
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .RequireRateLimiting(BidMatrixSecurityServiceCollectionExtensions.RecoveryInspectionRateLimitPolicy)
            .WithName("InspectAccountRecovery");

        auth.MapPost("/recovery/reset", ResetPasswordAsync)
            .AllowAnonymous()
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .RequireRateLimiting(BidMatrixSecurityServiceCollectionExtensions.RecoveryResetRateLimitPolicy)
            .WithName("ResetAccountPassword");

        var owner = endpoints.MapGroup("/owner/v1/account-recovery-links")
            .WithTags("Owner")
            .RequireAuthorization(BidMatrixPolicies.RecentPlatformOwner);

        owner.MapGet("/", ListRecoveriesAsync)
            .WithName("ListAccountRecoveryLinks");

        owner.MapPost("/", CreateRecoveryAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("CreateAccountRecoveryLink");

        owner.MapPost("/{recoveryId:guid}/revoke", RevokeRecoveryAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("RevokeAccountRecoveryLink");

        return endpoints;
    }

    private static async Task<IResult> ListSessionsAsync(
        ClaimsPrincipal principal,
        IUserSessionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var (userId, _, token) = GetSessionIdentity(principal);
            var sessions = await service.ListAsync(userId, token, cancellationToken);
            return Results.Ok(new UserSessionListResponse(sessions.Select(MapSession).ToArray()));
        }
        catch (AccountSecurityException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid sessionId,
        HttpContext context,
        IUserSessionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var (userId, currentSessionId, _) = GetSessionIdentity(context.User);
            var revoked = await service.RevokeAsync(
                sessionId,
                userId,
                sessionId == currentSessionId ? "user_revoked_current" : "user_revoked",
                context.TraceIdentifier,
                cancellationToken);
            if (!revoked)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Session not found",
                    detail: "The session was not found.");
            }

            if (sessionId == currentSessionId)
            {
                await context.SignOutAsync(BidMatrixAuthenticationSchemes.Cookie);
            }

            return Results.NoContent();
        }
        catch (AccountSecurityException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(
        HttpContext context,
        IUserSessionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var (userId, _, token) = GetSessionIdentity(context.User);
            var revokedCount = await service.RevokeOthersAsync(
                userId,
                token,
                context.TraceIdentifier,
                cancellationToken);
            return Results.Ok(new RevokeOtherSessionsResponse(revokedCount));
        }
        catch (AccountSecurityException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        HttpContext context,
        IAccountSecurityService service,
        ManagedOidcOptions managedOidcOptions,
        CancellationToken cancellationToken)
    {
        if (!managedOidcOptions.NativeLoginEnabled)
        {
            return NativeIdentityUnavailable("native_login_disabled", "Native password management is not enabled.");
        }

        try
        {
            var (userId, _, _) = GetSessionIdentity(context.User);
            await service.ChangePasswordAsync(new PasswordChangeCommand(
                userId,
                request.CurrentPassword,
                request.NewPassword,
                context.TraceIdentifier), cancellationToken);
            await context.SignOutAsync(BidMatrixAuthenticationSchemes.Cookie);
            return Results.NoContent();
        }
        catch (AccountSecurityException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ListRecoveriesAsync(
        ClaimsPrincipal principal,
        IAccountSecurityService service,
        ManagedOidcOptions managedOidcOptions,
        CancellationToken cancellationToken)
    {
        if (!managedOidcOptions.NativeRecoveryEnabled)
        {
            return NativeIdentityUnavailable("native_recovery_disabled", "Native account recovery is not enabled.");
        }

        var ownerId = GetUserId(principal);
        var recoveries = await service.ListRecoveriesAsync(ownerId, cancellationToken);
        return Results.Ok(new AccountRecoveryListResponse(recoveries.Select(MapRecovery).ToArray()));
    }

    private static async Task<IResult> CreateRecoveryAsync(
        [FromBody] CreateAccountRecoveryRequest request,
        HttpContext context,
        IAccountSecurityService service,
        ManagedOidcOptions managedOidcOptions,
        CancellationToken cancellationToken)
    {
        if (!managedOidcOptions.NativeRecoveryEnabled)
        {
            return NativeIdentityUnavailable("native_recovery_disabled", "Native account recovery is not enabled.");
        }

        try
        {
            var recovery = await service.CreateRecoveryAsync(new CreateAccountRecoveryCommand(
                GetUserId(context.User),
                request.Email,
                context.TraceIdentifier), cancellationToken);
            return Results.Created(
                $"/owner/v1/account-recovery-links/{recovery.Recovery.Id}",
                new CreatedAccountRecoveryResponse(
                    MapRecovery(recovery.Recovery),
                    recovery.Token,
                    recovery.RecoveryUrl));
        }
        catch (AccountSecurityException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> RevokeRecoveryAsync(
        Guid recoveryId,
        HttpContext context,
        IAccountSecurityService service,
        ManagedOidcOptions managedOidcOptions,
        CancellationToken cancellationToken)
    {
        if (!managedOidcOptions.NativeRecoveryEnabled)
        {
            return NativeIdentityUnavailable("native_recovery_disabled", "Native account recovery is not enabled.");
        }

        try
        {
            var recovery = await service.RevokeRecoveryAsync(new RevokeAccountRecoveryCommand(
                recoveryId,
                GetUserId(context.User),
                context.TraceIdentifier), cancellationToken);
            return Results.Ok(MapRecovery(recovery));
        }
        catch (AccountSecurityException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> InspectRecoveryAsync(
        [FromBody] InspectAccountRecoveryRequest request,
        IAccountSecurityService service,
        ManagedOidcOptions managedOidcOptions,
        CancellationToken cancellationToken)
    {
        if (!managedOidcOptions.NativeRecoveryEnabled)
        {
            return NativeIdentityUnavailable("native_recovery_disabled", "Native account recovery is not enabled.");
        }

        try
        {
            var inspection = await service.InspectRecoveryAsync(request.Token, cancellationToken);
            return Results.Ok(new AccountRecoveryInspectionResponse(
                inspection.RecoveryId.ToString(),
                inspection.Email,
                inspection.Status,
                inspection.ExpiresAt));
        }
        catch (AccountSecurityException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetAccountPasswordRequest request,
        HttpContext context,
        IAccountSecurityService service,
        ManagedOidcOptions managedOidcOptions,
        CancellationToken cancellationToken)
    {
        if (!managedOidcOptions.NativeRecoveryEnabled)
        {
            return NativeIdentityUnavailable("native_recovery_disabled", "Native account recovery is not enabled.");
        }

        try
        {
            await service.ResetPasswordAsync(new ResetAccountPasswordCommand(
                request.Token,
                request.NewPassword,
                context.TraceIdentifier), cancellationToken);
            return Results.Ok(new AccountRecoveryCompletedResponse("password_reset"));
        }
        catch (AccountSecurityException exception)
        {
            return Problem(exception);
        }
    }

    private static (Guid UserId, Guid SessionId, string Token) GetSessionIdentity(ClaimsPrincipal principal)
    {
        var userId = GetUserId(principal);
        var sessionIdValue = principal.FindFirstValue(BidMatrixClaimTypes.SessionId);
        var token = principal.FindFirstValue(BidMatrixClaimTypes.SessionToken);
        if (!Guid.TryParse(sessionIdValue, out var sessionId) || string.IsNullOrEmpty(token))
        {
            throw new AccountSecurityException("invalid_session", "The current session is invalid.", 401);
        }

        return (userId, sessionId, token);
    }

    private static Guid GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new AccountSecurityException("invalid_session", "The current session is invalid.", 401);
    }

    private static UserSessionResponse MapSession(UserSessionRecord session) => new(
        session.Id.ToString(),
        session.CreatedAt,
        session.LastSeenAt,
        session.AbsoluteExpiresAt,
        session.RevokedAt,
        session.RevokedReason,
        session.Status,
        session.IsCurrent,
        session.Version);

    private static AccountRecoveryResponse MapRecovery(AccountRecoveryRecord recovery) => new(
        recovery.Id.ToString(),
        recovery.UserId.ToString(),
        recovery.Email,
        recovery.DisplayName,
        recovery.Status,
        recovery.CreatedAt,
        recovery.ExpiresAt,
        recovery.UsedAt,
        recovery.RevokedAt,
        recovery.Version);

    private static IResult Problem(AccountSecurityException exception) => Results.Problem(
        statusCode: exception.StatusCode,
        title: exception.Code,
        detail: exception.Message);

    private static IResult NativeIdentityUnavailable(string code, string detail) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: code,
        detail: detail);
}
