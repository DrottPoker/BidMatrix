namespace BidMatrix.Contracts.Identity;

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthenticationConfigurationResponse(
    bool ManagedOidcEnabled,
    string ManagedOidcProviderName,
    bool NativeLoginEnabled,
    bool NativeRecoveryEnabled,
    bool IdentityTransitionMode);

public sealed record LogoutResponse(bool Success);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UserSessionResponse(
    string Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset AbsoluteExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    string Status,
    bool IsCurrent,
    int Version);

public sealed record UserSessionListResponse(IReadOnlyList<UserSessionResponse> Sessions);

public sealed record RevokeOtherSessionsResponse(int RevokedSessionCount);

public sealed record FederatedIdentityResponse(
    string Id,
    string ProviderName,
    string Issuer,
    string EmailAtLink,
    string Status,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastAuthenticatedAt,
    DateTimeOffset? RevokedAt,
    int Version);

public sealed record FederatedIdentityListResponse(
    IReadOnlyList<FederatedIdentityResponse> Identities,
    bool NativePasswordEnabled);

public sealed record RevokeFederatedIdentityResponse(
    FederatedIdentityResponse Identity,
    int RevokedSessionCount);

public sealed record CreateAccountRecoveryRequest(string Email);

public sealed record AccountRecoveryResponse(
    string Id,
    string UserId,
    string Email,
    string? DisplayName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? RevokedAt,
    int Version);

public sealed record AccountRecoveryListResponse(IReadOnlyList<AccountRecoveryResponse> Recoveries);

public sealed record CreatedAccountRecoveryResponse(
    AccountRecoveryResponse Recovery,
    string Token,
    string RecoveryUrl);

public sealed record InspectAccountRecoveryRequest(string Token);

public sealed record AccountRecoveryInspectionResponse(
    string RecoveryId,
    string Email,
    string Status,
    DateTimeOffset ExpiresAt);

public sealed record ResetAccountPasswordRequest(string Token, string NewPassword);

public sealed record AccountRecoveryCompletedResponse(string Status);

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
