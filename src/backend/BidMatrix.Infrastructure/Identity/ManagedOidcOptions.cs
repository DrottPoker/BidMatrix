using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BidMatrix.Infrastructure.Identity;

public sealed class ManagedOidcOptions
{
    private ManagedOidcOptions(
        bool enabled,
        string providerName,
        Uri? publicApiBaseUri,
        Uri? authority,
        string? clientId,
        string? clientSecret,
        bool requireHttpsMetadata,
        bool nativeLoginEnabled,
        bool nativeRecoveryEnabled,
        bool transitionMode)
    {
        Enabled = enabled;
        ProviderName = providerName;
        PublicApiBaseUri = publicApiBaseUri;
        Authority = authority;
        ClientId = clientId;
        ClientSecret = clientSecret;
        RequireHttpsMetadata = requireHttpsMetadata;
        NativeLoginEnabled = nativeLoginEnabled;
        NativeRecoveryEnabled = nativeRecoveryEnabled;
        TransitionMode = transitionMode;
    }

    public bool Enabled { get; }

    public string ProviderName { get; }

    public Uri? PublicApiBaseUri { get; }

    public Uri? Authority { get; }

    public string? ClientId { get; }

    public string? ClientSecret { get; }

    public bool RequireHttpsMetadata { get; }

    public bool NativeLoginEnabled { get; }

    public bool NativeRecoveryEnabled { get; }

    public bool TransitionMode { get; }

    public static ManagedOidcOptions FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var enabled = ParseBoolean(configuration, "BIDMATRIX_OIDC_ENABLED", false);
        var transitionMode = ParseBoolean(configuration, "BIDMATRIX_IDENTITY_TRANSITION_MODE", false);
        var nativeLoginEnabled = ParseBoolean(
            configuration,
            "BIDMATRIX_NATIVE_LOGIN_ENABLED",
            environment.IsDevelopment());
        var nativeRecoveryEnabled = ParseBoolean(
            configuration,
            "BIDMATRIX_NATIVE_RECOVERY_ENABLED",
            environment.IsDevelopment());
        var requireHttpsMetadata = ParseBoolean(
            configuration,
            "BIDMATRIX_OIDC_REQUIRE_HTTPS_METADATA",
            !environment.IsDevelopment());
        var providerName = ReadBoundedText(
            configuration,
            "BIDMATRIX_OIDC_PROVIDER_NAME",
            enabled ? null : "Managed identity",
            2,
            80);
        var publicApiBaseUri = ParsePublicApiBaseUri(configuration, environment, enabled);
        var authority = ParseAuthority(configuration, environment, enabled, requireHttpsMetadata);
        var clientId = ReadBoundedText(
            configuration,
            "BIDMATRIX_OIDC_CLIENT_ID",
            enabled ? null : string.Empty,
            enabled ? 1 : 0,
            255);
        var clientSecret = ReadBoundedText(
            configuration,
            "BIDMATRIX_OIDC_CLIENT_SECRET",
            enabled ? null : string.Empty,
            enabled ? 1 : 0,
            2048,
            trim: false);
        if (transitionMode && !enabled)
        {
            throw new InvalidOperationException(
                "BIDMATRIX_IDENTITY_TRANSITION_MODE requires BIDMATRIX_OIDC_ENABLED=true.");
        }

        if (!environment.IsDevelopment() && !enabled)
        {
            throw new InvalidOperationException(
                "BIDMATRIX_OIDC_ENABLED must be true outside Development.");
        }

        if (!environment.IsDevelopment() &&
            (nativeLoginEnabled || nativeRecoveryEnabled) &&
            !transitionMode)
        {
            throw new InvalidOperationException(
                "Native login or recovery outside Development requires explicit identity transition mode.");
        }

        if (!environment.IsDevelopment() && nativeRecoveryEnabled && !nativeLoginEnabled)
        {
            throw new InvalidOperationException(
                "Native recovery cannot be enabled when native login is disabled outside Development.");
        }

        return new ManagedOidcOptions(
            enabled,
            providerName,
            publicApiBaseUri,
            authority,
            clientId,
            clientSecret,
            requireHttpsMetadata,
            nativeLoginEnabled,
            nativeRecoveryEnabled,
            transitionMode);
    }

    public override string ToString() =>
        $"ManagedOidcOptions {{ Enabled = {Enabled}, ProviderName = {ProviderName}, " +
        $"PublicApiBaseUri = {PublicApiBaseUri}, Authority = {Authority}, " +
        $"ClientIdConfigured = {!string.IsNullOrEmpty(ClientId)}, " +
        $"ClientSecretConfigured = {!string.IsNullOrEmpty(ClientSecret)}, " +
        $"NativeLoginEnabled = {NativeLoginEnabled}, NativeRecoveryEnabled = {NativeRecoveryEnabled}, " +
        $"TransitionMode = {TransitionMode} }}";

    private static Uri? ParsePublicApiBaseUri(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool enabled)
    {
        var value = configuration["BIDMATRIX_API_PUBLIC_BASE_URL"]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            if (!enabled)
            {
                return null;
            }

            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "BIDMATRIX_API_PUBLIC_BASE_URL is required when managed OIDC is enabled outside Development.");
            }

            value = "http://localhost:8080";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "BIDMATRIX_API_PUBLIC_BASE_URL must be an absolute HTTP or HTTPS origin without credentials, path, query, or fragment.");
        }

        if (!environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "BIDMATRIX_API_PUBLIC_BASE_URL must use HTTPS outside Development.");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
    }

    private static Uri? ParseAuthority(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool enabled,
        bool requireHttpsMetadata)
    {
        var value = configuration["BIDMATRIX_OIDC_AUTHORITY"]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            if (enabled)
            {
                throw new InvalidOperationException(
                    "BIDMATRIX_OIDC_AUTHORITY is required when managed OIDC is enabled.");
            }

            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var authority) ||
            !string.IsNullOrEmpty(authority.UserInfo) ||
            !string.IsNullOrEmpty(authority.Query) ||
            !string.IsNullOrEmpty(authority.Fragment) ||
            authority.Scheme != Uri.UriSchemeHttps &&
            authority.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException(
                "BIDMATRIX_OIDC_AUTHORITY must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.");
        }

        if ((requireHttpsMetadata || !environment.IsDevelopment()) && authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "BIDMATRIX_OIDC_AUTHORITY must use HTTPS when HTTPS metadata is required.");
        }

        return new Uri(authority.AbsoluteUri.TrimEnd('/'), UriKind.Absolute);
    }

    private static bool ParseBoolean(
        IConfiguration configuration,
        string key,
        bool defaultValue)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : bool.TryParse(value, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"{key} must be true or false.");
    }

    private static string ReadBoundedText(
        IConfiguration configuration,
        string key,
        string? defaultValue,
        int minimumLength,
        int maximumLength,
        bool trim = true)
    {
        var configured = configuration[key];
        var value = configured ?? defaultValue
            ?? throw new InvalidOperationException($"{key} is required when managed OIDC is enabled.");
        if (trim)
        {
            value = value.Trim();
        }

        if (value.Length < minimumLength ||
            value.Length > maximumLength ||
            value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"{key} must contain {minimumLength} to {maximumLength} characters without control characters.");
        }

        return value;
    }
}
