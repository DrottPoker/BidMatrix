namespace BidMatrix.Application.Identity;

public enum AuthenticationStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
    Disabled,
}

public sealed record OrganizationMembership(Guid OrganizationId, string Role);

public sealed record AuthenticatedUser(
    Guid UserId,
    string Email,
    string? DisplayName,
    Guid SecurityStamp,
    IReadOnlyList<OrganizationMembership> Memberships,
    IReadOnlyList<string> PlatformRoles);

public sealed record AuthenticationAttempt(AuthenticationStatus Status, AuthenticatedUser? User)
{
    public static AuthenticationAttempt Failure(AuthenticationStatus status) => new(status, null);

    public static AuthenticationAttempt Success(AuthenticatedUser user) => new(AuthenticationStatus.Succeeded, user);
}

public interface IUserAuthenticationService
{
    Task<AuthenticationAttempt> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}
