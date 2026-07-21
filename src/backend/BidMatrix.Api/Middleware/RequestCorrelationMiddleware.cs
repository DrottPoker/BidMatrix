using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BidMatrix.Api.Middleware;

public sealed partial class RequestCorrelationMiddleware(
    RequestDelegate next,
    ILogger<RequestCorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestedId = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValidCorrelationId(requestedId)
            ? requestedId
            : Activity.Current?.TraceId.ToString() ?? Guid.CreateVersion7().ToString();

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static bool IsValidCorrelationId(string value) =>
        value.Length is >= 8 and <= 128 && CorrelationIdPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}
