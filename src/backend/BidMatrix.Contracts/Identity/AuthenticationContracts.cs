namespace BidMatrix.Contracts.Identity;

public sealed record LoginRequest(string Email, string Password);

public sealed record CsrfTokenResponse(string Token, string HeaderName);

public sealed record OrganizationMembershipResponse(string OrganizationId, string Role);

public sealed record CurrentUserResponse(
    string UserId,
    string Email,
    string? DisplayName,
    IReadOnlyList<OrganizationMembershipResponse> Organizations,
    IReadOnlyList<string> PlatformRoles);

public sealed record CurrentOrganizationResponse(string OrganizationId, string Role);

public sealed record OwnerDashboardAccessResponse(
    string CapabilityStatus,
    string Message,
    DateTimeOffset GeneratedAt);

public sealed record InternalServiceStatusResponse(string Service, string Status);
