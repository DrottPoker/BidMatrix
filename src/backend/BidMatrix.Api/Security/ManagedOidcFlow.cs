namespace BidMatrix.Api.Security;

public static class ManagedOidcFlow
{
    public const string ModeKey = "bidmatrix:oidc:mode";
    public const string ReturnUrlKey = "bidmatrix:oidc:return_url";
    public const string UserIdKey = "bidmatrix:oidc:user_id";
    public const string SessionIdKey = "bidmatrix:oidc:session_id";
    public const string SecurityStampKey = "bidmatrix:oidc:security_stamp";
    public const string LoginMode = "login";
    public const string LinkMode = "link";

    public static string ErrorPath(string? mode) => mode switch
    {
        LinkMode => "/app/account",
        _ => "/login",
    };

    public static string DefaultReturnUrl(string? mode) => mode switch
    {
        LinkMode => "/app/account",
        _ => "/",
    };
}
