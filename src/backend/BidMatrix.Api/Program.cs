using System.Text.Json;
using System.Text.Json.Serialization;
using BidMatrix.Api.Endpoints;
using BidMatrix.Api.Middleware;
using BidMatrix.Api.Security;
using BidMatrix.Contracts.System;
using BidMatrix.Database.Schema;
using BidMatrix.Infrastructure.Identity;
using BidMatrix.Infrastructure.Analyses;
using BidMatrix.Infrastructure.Tools;
using BidMatrix.Infrastructure.Agents;
using BidMatrix.Infrastructure.Owner;
using BidMatrix.Infrastructure.Internal;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;

if (args is ["--healthcheck"])
{
    return await RunHealthCheckAsync();
}

var builder = WebApplication.CreateBuilder(args);

const long maximumConfiguredUploadBytes = 100 * 1024 * 1024;
const long multipartOverheadBytes = 1024 * 1024;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maximumConfiguredUploadBytes + multipartOverheadBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maximumConfiguredUploadBytes + multipartOverheadBytes;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.MaxDepth = 32;
});
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddOpenApi();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = builder.Environment.IsDevelopment()
        ? "bidmatrix.csrf"
        : "__Host-bidmatrix.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("BidMatrix");
if (builder.Configuration["DATA_PROTECTION_KEYS_PATH"] is { Length: > 0 } dataProtectionPath)
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
}

builder.Services.AddHealthChecks();
builder.Services.AddBidMatrixDatabase(builder.Configuration);
builder.Services.AddBidMatrixIdentity();
builder.Services.AddBidMatrixAnalysis(builder.Configuration, builder.Environment);
builder.Services.AddBidMatrixToolGateway(builder.Configuration);
builder.Services.AddBidMatrixAgentRuntime();
builder.Services.AddBidMatrixOwnerConsole();
builder.Services.AddBidMatrixInternalFoundation();
builder.Services.AddBidMatrixApiSecurity(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    await Results.Problem(
            statusCode: response.StatusCode,
            title: ReasonPhrases.GetReasonPhrase(response.StatusCode))
        .ExecuteAsync(statusContext.HttpContext);
});
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors(BidMatrixSecurityServiceCollectionExtensions.WebCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet(
        "/api",
        () => Results.Ok(new ServiceInfoResponse("BidMatrix API", "0.1.0", "foundation")))
    .AllowAnonymous()
    .WithName("GetServiceInfo");

app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
    })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready")
    .AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapBidMatrixIdentityEndpoints();
app.MapBidMatrixAnalysisEndpoints();
app.MapBidMatrixToolGatewayEndpoints();
app.MapBidMatrixAgentRuntimeEndpoints();
app.MapBidMatrixOwnerConsoleEndpoints();
app.MapBidMatrixInternalFoundationEndpoints();

await app.RunAsync();
return 0;

static async Task<int> RunHealthCheckAsync()
{
    using var client = new HttpClient
    {
        BaseAddress = new Uri("http://127.0.0.1:8080"),
        Timeout = Timeout.InfiniteTimeSpan,
    };
    using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    try
    {
        using var response = await client.GetAsync("/health/ready", cancellationSource.Token);
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
    catch (OperationCanceledException)
    {
        return 1;
    }
}

public partial class Program;
