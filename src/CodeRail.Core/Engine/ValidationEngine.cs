using CodeRail.Evidence;
using CodeRail.Policy;
using CodeRail.Tooling;
using Microsoft.Extensions.Logging;

namespace CodeRail.Engine;

/// <summary>
/// Coordinates the execution of a validation profile's steps and hands the collected evidence to
/// <see cref="PolicyEvaluator"/> for a final verdict. See <c>docs/HIGH_LEVEL_PLAN.md</c> §6.1
/// (Validation Engine). Deliberately doesn't know about YAML/<c>CodeRail.Configuration</c> -
/// callers resolve a profile to a step list and a <see cref="QualityProfile"/> first.
/// </summary>
public sealed class ValidationEngine(
    IReadOnlyDictionary<string, IToolExecutor> executors,
    PolicyEvaluator policyEvaluator,
    ILogger<ValidationEngine> logger)
{
    public async Task<GateResult> RunAsync(
        ToolContext context,
        IReadOnlyList<string> steps,
        QualityProfile quality,
        CancellationToken cancellationToken)
    {
        var results = new List<ToolResult>();

        foreach (var step in steps)
        {
            if (!executors.TryGetValue(step, out var executor))
            {
                logger.LogWarning("No executor registered for validation step '{Step}' - skipping", step);
                continue;
            }

            logger.LogInformation("Running step '{Step}'", step);
            var result = await executor.ExecuteAsync(context, cancellationToken);
            results.Add(result);
            logger.LogInformation("Step '{Step}' finished: {Status} ({FindingCount} finding(s))", step, result.Status, result.Findings.Count);

            // There's no value running tests/coverage/CodeGuard against code that doesn't
            // compile (§6.1) - stop here and let the policy evaluate what we do have.
            if (step == ValidationSteps.Build && result.Status != ValidationStatus.Passed)
            {
                logger.LogWarning("Build failed - short-circuiting the remaining validation steps");
                break;
            }
        }

        return policyEvaluator.Evaluate(results, quality);
    }
}
