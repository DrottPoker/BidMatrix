using System.Text.Json;
using BidMatrix.Application.Tools;

namespace BidMatrix.Infrastructure.Tools;

internal static class ToolArgumentValidator
{
    private static readonly HashSet<string> NoRequiredArgumentTools = new(StringComparer.Ordinal)
    {
        "context.getCompanyConstitution",
        "context.getProductFacts",
        "context.getMetricsSnapshot",
        "knowledge.search",
    };

    public static void Validate(string toolKey, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Tool arguments must be a JSON object.");
        }

        if (NoRequiredArgumentTools.Contains(toolKey))
        {
            return;
        }

        switch (toolKey)
        {
            case "context.getTask":
                RequireGuid(arguments, "taskId");
                break;
            case "context.getAnalysis":
                RequireGuid(arguments, "analysisId");
                break;
            case "artifact.read":
                RequireGuid(arguments, "artifactId");
                break;
            case "task.create":
                RequireString(arguments, "title", 200);
                RequireOptionalString(arguments, "description", 10_000);
                break;
            case "task.addNote":
                RequireGuid(arguments, "taskId");
                RequireString(arguments, "note", 20_000);
                break;
            case "artifact.createDraft":
                RequireString(arguments, "title", 200);
                RequireProperty(arguments, "content");
                break;
            case "approval.request":
                RequireString(arguments, "actionType", 100);
                RequireString(arguments, "summary", 500);
                RequireProperty(arguments, "payload");
                break;
            case "agentRun.addFinding":
                RequireString(arguments, "summary", 2_000);
                break;
            case "repo.createWorktree":
                RequireString(arguments, "baseRevision", 200);
                break;
            case "repo.readFile":
                RequireString(arguments, "path", 500);
                break;
            case "repo.search":
                RequireString(arguments, "query", 200);
                RequireOptionalString(arguments, "path", 500);
                break;
            case "repo.getStatus":
            case "repo.getDiff":
                break;
            case "repo.writeFile":
                RequireString(arguments, "path", 500);
                RequireString(arguments, "content", 262_144);
                break;
            case "repo.runAllowlistedCommand":
                RequireString(arguments, "executable", 100);
                RequireStringArray(arguments, "arguments", 20, 200);
                break;
            case "repo.createDiffArtifact":
                RequireOptionalString(arguments, "title", 200);
                break;
            case "email.send":
                RequireString(arguments, "to", 320);
                RequireString(arguments, "subject", 998);
                RequireString(arguments, "body", 100_000);
                break;
            default:
                break;
        }
    }

    public static string RequireString(JsonElement arguments, string propertyName, int maxLength)
    {
        if (!arguments.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) ||
            value.GetString()!.Length > maxLength)
        {
            throw Invalid($"{propertyName} must be a non-empty string of at most {maxLength} characters.");
        }

        return value.GetString()!;
    }

    public static string? RequireOptionalString(JsonElement arguments, string propertyName, int maxLength)
    {
        if (!arguments.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > maxLength)
        {
            throw Invalid($"{propertyName} must be a string of at most {maxLength} characters.");
        }

        return value.GetString();
    }

    public static Guid RequireGuid(JsonElement arguments, string propertyName)
    {
        var value = RequireString(arguments, propertyName, 64);
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw Invalid($"{propertyName} must be a valid UUID.");
    }

    public static IReadOnlyList<string> RequireStringArray(
        JsonElement arguments,
        string propertyName,
        int maximumItems,
        int maximumItemLength)
    {
        if (!arguments.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() > maximumItems)
        {
            throw Invalid($"{propertyName} must be an array with at most {maximumItems} items.");
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString()!.Length > maximumItemLength)
            {
                throw Invalid($"Every {propertyName} item must be a bounded string.");
            }
            result.Add(item.GetString()!);
        }
        return result;
    }

    private static void RequireProperty(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out _))
        {
            throw Invalid($"{propertyName} is required.");
        }
    }

    private static ToolGatewayException Invalid(string message) =>
        new("invalid_tool_arguments", message, 400);
}
