using System.Security.Claims;
using BidMatrix.Api.Security;
using BidMatrix.Application.Identity;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BidMatrix.Api.IntegrationTests;

public sealed class ManagedOidcSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PrincipalFactoryHashesProviderSubjectAndKeepsRolesOutOfIdentityClaims()
    {
        var factory = CreatePrincipalFactory();
        var principal = CreatePrincipal();

        var externalPrincipal = factory.CreateExternalPrincipal(
            principal,
            "https://identity.example.invalid");
        var profile = factory.Create(externalPrincipal);

        Assert.Equal(
            FederatedSubjectUtility.Hash(
                "https://identity.example.invalid",
                "provider-subject"),
            profile.SubjectHash);
        Assert.Empty(externalPrincipal.FindAll("sub"));
        Assert.Empty(externalPrincipal.FindAll("acr"));
        Assert.Empty(externalPrincipal.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public void PrincipalFactoryRequiresVerifiedEmailAndRecentAuthenticationTime()
    {
        var factory = CreatePrincipalFactory();
        var unverified = CreatePrincipal(emailVerified: false);
        var old = CreatePrincipal(
            authenticationTime: Now.AddMinutes(-16));

        var unverifiedException = Assert.Throws<FederatedIdentityException>(() =>
            factory.CreateExternalPrincipal(unverified, "https://identity.example.invalid"));
        Assert.Equal("invalid_identity", unverifiedException.Code);
        var oldException = Assert.Throws<FederatedIdentityException>(() =>
            factory.CreateExternalPrincipal(old, "https://identity.example.invalid"));
        Assert.Equal("authentication_too_old", oldException.Code);
    }

    [Fact]
    public void ProductionConfigurationFailsClosedWithoutManagedOidc()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ManagedOidcOptions.FromConfiguration(configuration, new TestHostEnvironment("Production")));

        Assert.Contains("BIDMATRIX_OIDC_ENABLED", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionConfigurationRequiresExplicitTransitionForNativeFallback()
    {
        var values = CreateProductionValues();
        values["BIDMATRIX_NATIVE_LOGIN_ENABLED"] = "true";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ManagedOidcOptions.FromConfiguration(configuration, new TestHostEnvironment("Production")));

        Assert.Contains("transition mode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigurationSummaryNeverContainsClientSecret()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CreateProductionValues())
            .Build();

        var options = ManagedOidcOptions.FromConfiguration(
            configuration,
            new TestHostEnvironment("Production"));

        Assert.True(options.Enabled);
        Assert.False(options.NativeLoginEnabled);
        Assert.DoesNotContain("test-client-secret", options.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OidcHandlerUsesServerSideCodeFlowWithPkceAndNoSavedTokens()
    {
        using var provider = CreateSecurityProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(BidMatrixAuthenticationSchemes.OpenIdConnect);

        Assert.Equal(BidMatrixAuthenticationSchemes.ExternalCookie, options.SignInScheme);
        Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
        Assert.Equal(OpenIdConnectResponseMode.Query, options.ResponseMode);
        Assert.True(options.UsePkce);
        Assert.False(options.SaveTokens);
        Assert.False(options.GetClaimsFromUserInfoEndpoint);
        Assert.False(options.MapInboundClaims);
        Assert.Equal(TimeSpan.FromMinutes(15), options.MaxAge);
        Assert.Contains(OpenIdConnectScope.OpenId, options.Scope);
        Assert.Contains(OpenIdConnectScope.Profile, options.Scope);
        Assert.Contains(OpenIdConnectScope.Email, options.Scope);
    }

    [Fact]
    public async Task PlatformOwnerPolicyUsesTheBidMatrixRoleForEverySupportedSignInMethod()
    {
        using var provider = CreateSecurityProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var passwordOwner = CreateBidMatrixPrincipal(BidMatrixAuthenticationMethods.Password);
        var oidcOwner = CreateBidMatrixPrincipal(BidMatrixAuthenticationMethods.Oidc);

        var passwordResult = await authorization.AuthorizeAsync(
            passwordOwner,
            null,
            BidMatrixPolicies.PlatformOwner);
        var oidcResult = await authorization.AuthorizeAsync(
            oidcOwner,
            null,
            BidMatrixPolicies.PlatformOwner);

        Assert.True(passwordResult.Succeeded);
        Assert.True(oidcResult.Succeeded);
    }

    private static ManagedOidcPrincipalFactory CreatePrincipalFactory()
    {
        var securityOptions = new IdentitySecurityOptions(
            new Uri("https://app.example.invalid"),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(8),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(15));
        return new ManagedOidcPrincipalFactory(
            new FixedTimeProvider(Now),
            securityOptions);
    }

    private static ServiceProvider CreateSecurityProvider()
    {
        var values = CreateProductionValues();
        values["BIDMATRIX_PUBLIC_BASE_URL"] = "https://app.example.invalid";
        values["INTERNAL_SERVICE_TOKEN"] = "test-internal-token";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = new TestHostEnvironment("Production");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddBidMatrixIdentity(configuration, environment);
        services.AddBidMatrixApiSecurity(configuration, environment);
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateBidMatrixPrincipal(string authenticationMethod)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
            new Claim(ClaimTypes.Role, "platform_owner"),
            new Claim(BidMatrixClaimTypes.AuthenticationMethod, authenticationMethod),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, BidMatrixAuthenticationSchemes.Cookie));
    }

    private static ClaimsPrincipal CreatePrincipal(
        bool emailVerified = true,
        DateTimeOffset? authenticationTime = null)
    {
        var claims = new[]
        {
            new Claim("iss", "https://identity.example.invalid"),
            new Claim("sub", "provider-subject"),
            new Claim("email", "owner@example.invalid"),
            new Claim("email_verified", emailVerified ? "true" : "false"),
            new Claim("auth_time", (authenticationTime ?? Now).ToUnixTimeSeconds().ToString()),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc"));
    }

    private static Dictionary<string, string?> CreateProductionValues() => new()
    {
        ["BIDMATRIX_OIDC_ENABLED"] = "true",
        ["BIDMATRIX_API_PUBLIC_BASE_URL"] = "https://api.example.invalid",
        ["BIDMATRIX_OIDC_PROVIDER_NAME"] = "Test managed identity",
        ["BIDMATRIX_OIDC_AUTHORITY"] = "https://identity.example.invalid",
        ["BIDMATRIX_OIDC_CLIENT_ID"] = "test-client",
        ["BIDMATRIX_OIDC_CLIENT_SECRET"] = "test-client-secret",
        ["BIDMATRIX_OIDC_REQUIRE_HTTPS_METADATA"] = "true",
        ["BIDMATRIX_IDENTITY_TRANSITION_MODE"] = "false",
        ["BIDMATRIX_NATIVE_LOGIN_ENABLED"] = "false",
        ["BIDMATRIX_NATIVE_RECOVERY_ENABLED"] = "false",
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "BidMatrix.Api.IntegrationTests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
