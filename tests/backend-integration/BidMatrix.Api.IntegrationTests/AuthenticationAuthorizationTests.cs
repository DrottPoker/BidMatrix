using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BidMatrix.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BidMatrix.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AuthenticationAuthorizationTests(DatabaseFixture database)
{
    private const string OwnerEmail = "owner@example.invalid";
    private const string OwnerPassword = "phase-three-owner-password";
    private const string InternalServiceToken = "phase-three-internal-service-token";

    [Fact]
    public async Task UnauthenticatedRequestsAreDenied()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owner/v1/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/v1/status")).StatusCode);
    }

    [Fact]
    public async Task LoginRequiresCsrfToken()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = CreateCookieClient(factory);
        await client.GetAsync("/v1/auth/csrf");

        using var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new LoginRequest(OwnerEmail, OwnerPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCanAuthenticateAndAccessOwnerRoutes()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = CreateCookieClient(factory);

        await LoginAsync(client, OwnerEmail, OwnerPassword);

        using var ownerResponse = await client.GetAsync("/owner/v1/dashboard");
        using var currentUserResponse = await client.GetAsync("/v1/me");
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, currentUserResponse.StatusCode);

        var currentUser = await currentUserResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(currentUser);
        Assert.Contains("platform_owner", currentUser.PlatformRoles);
    }

    [Fact]
    public async Task CustomerCannotAccessOwnerOrInternalRoutes()
    {
        var customer = await CreateCustomerAsync();
        using var factory = new BidMatrixApiFactory(database);
        using var client = CreateCookieClient(factory);

        await LoginAsync(client, customer.Email, customer.Password);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/owner/v1/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/v1/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/organizations/current")).StatusCode);
    }

    [Fact]
    public async Task InternalServiceCannotAccessOwnerRoutes()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalServiceToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/internal/v1/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owner/v1/dashboard")).StatusCode);
    }

    [Fact]
    public async Task LogoutRequiresCsrfAndInvalidatesSession()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = CreateCookieClient(factory);
        await LoginAsync(client, OwnerEmail, OwnerPassword);

        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/me")).StatusCode);

        await AddCsrfTokenAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/me")).StatusCode);
    }

    [Fact]
    public async Task DevelopmentOpenApiAndCorrelationHeaderAreAvailable()
    {
        using var factory = new BidMatrixApiFactory(database);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "phase3-test-correlation");

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("phase3-test-correlation", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    private static HttpClient CreateCookieClient(BidMatrixApiFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        await AddCsrfTokenAsync(client);
        using var response = await client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AddCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Remove(token.HeaderName);
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);
    }

    private async Task<(string Email, string Password)> CreateCustomerAsync()
    {
        var userId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();
        var email = $"customer-{userId:N}@example.invalid";
        const string password = "phase-three-customer-password";
        var passwordHasher = new PasswordHasher<TestPasswordSubject>();
        var passwordHash = passwordHasher.HashPassword(new TestPasswordSubject(userId), password);

        await using var connection = await database.MigrationDataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = """
                insert into users (id, email, normalized_email, display_name, status, created_at, updated_at)
                values ($1, $2, $3, 'Test Customer', 'active', now(), now())
                """;
            userCommand.Parameters.AddWithValue(userId);
            userCommand.Parameters.AddWithValue(email);
            userCommand.Parameters.AddWithValue(email.ToUpperInvariant());
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (var organizationCommand = connection.CreateCommand())
        {
            organizationCommand.Transaction = transaction;
            organizationCommand.CommandText = """
                insert into organizations (id, name, slug, status, created_at, updated_at)
                values ($1, 'Test Customer Organization', $2, 'active', now(), now())
                """;
            organizationCommand.Parameters.AddWithValue(organizationId);
            organizationCommand.Parameters.AddWithValue($"customer-{organizationId:N}");
            await organizationCommand.ExecuteNonQueryAsync();
        }

        await using (var membershipCommand = connection.CreateCommand())
        {
            membershipCommand.Transaction = transaction;
            membershipCommand.CommandText = """
                insert into organization_memberships (id, organization_id, user_id, role, created_at)
                values ($1, $2, $3, 'member', now())
                """;
            membershipCommand.Parameters.AddWithValue(membershipId);
            membershipCommand.Parameters.AddWithValue(organizationId);
            membershipCommand.Parameters.AddWithValue(userId);
            await membershipCommand.ExecuteNonQueryAsync();
        }

        await using (var credentialCommand = connection.CreateCommand())
        {
            credentialCommand.Transaction = transaction;
            credentialCommand.CommandText = """
                insert into user_credentials (
                    user_id,
                    password_hash,
                    security_stamp,
                    password_changed_at,
                    created_at,
                    updated_at,
                    version
                )
                values ($1, $2, $3, now(), now(), now(), 1)
                """;
            credentialCommand.Parameters.AddWithValue(userId);
            credentialCommand.Parameters.AddWithValue(passwordHash);
            credentialCommand.Parameters.AddWithValue(Guid.CreateVersion7());
            await credentialCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return (email, password);
    }

    private sealed record TestPasswordSubject(Guid UserId);
}
