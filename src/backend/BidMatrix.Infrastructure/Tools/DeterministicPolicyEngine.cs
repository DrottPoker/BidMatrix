using BidMatrix.Application.Tools;

namespace BidMatrix.Infrastructure.Tools;

public sealed class DeterministicPolicyEngine : IPolicyEngine
{
    private static readonly HashSet<string> RepositoryTools = new(StringComparer.Ordinal)
    {
        "repo.readFile",
        "repo.search",
        "repo.getStatus",
        "repo.getDiff",
        "repo.createWorktree",
        "repo.writeFile",
        "repo.runAllowlistedCommand",
        "repo.createDiffArtifact",
    };

    public PolicyEvaluation Evaluate(PolicyEvaluationContext context)
    {
        if (!GetControl(context.Controls, "allAgentsEnabled"))
        {
            return Denied("all_agents_disabled");
        }

        if (context.Tool.RiskLevel == "prohibited")
        {
            return Denied("prohibited_action");
        }

        if (RepositoryTools.Contains(context.Tool.ToolKey))
        {
            if (!string.Equals(context.AgentKey, "engineering", StringComparison.Ordinal) ||
                !string.Equals(context.TaskType, "engineering", StringComparison.Ordinal) ||
                !context.OwnerCreatedTask)
            {
                return Denied("engineering_task_required");
            }

            if (!GetControl(context.Controls, "engineeringWritesEnabled"))
            {
                return Denied("engineering_writes_disabled");
            }
        }

        if (context.Tool.SideEffectClass is "external_reversible" or "external_material" or "destructive")
        {
            return new PolicyEvaluation(
                ToolDecisions.ApprovalRequired,
                "owner_approval_required",
                context.Tool.Enabled && GetControl(context.Controls, "externalToolExecutionEnabled"));
        }

        if (!context.Tool.Enabled || context.Tool.ApprovalMode == "disabled")
        {
            return new PolicyEvaluation(ToolDecisions.Disabled, "tool_adapter_disabled", false);
        }

        if (context.Tool.ApprovalMode == "always")
        {
            return new PolicyEvaluation(
                ToolDecisions.ApprovalRequired,
                "owner_approval_required",
                context.Tool.Enabled);
        }

        return context.Tool.SideEffectClass switch
        {
            "read_only" => new PolicyEvaluation(ToolDecisions.Allowed, "internal_read_allowed", true),
            "internal_write" => new PolicyEvaluation(ToolDecisions.Allowed, "internal_write_allowed", true),
            _ => Denied("unsupported_side_effect_class"),
        };
    }

    private static bool GetControl(IReadOnlyDictionary<string, bool> controls, string key) =>
        controls.TryGetValue(key, out var enabled) && enabled;

    private static PolicyEvaluation Denied(string reasonCode) =>
        new(ToolDecisions.Denied, reasonCode, false);
}
