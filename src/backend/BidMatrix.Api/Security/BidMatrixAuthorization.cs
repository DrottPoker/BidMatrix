namespace BidMatrix.Api.Security;

public static class BidMatrixPolicies
{
    public const string AuthenticatedUser = "authenticated-user";
    public const string Customer = "customer";
    public const string PlatformOwner = "platform-owner";
    public const string InternalService = "internal-service";
}
