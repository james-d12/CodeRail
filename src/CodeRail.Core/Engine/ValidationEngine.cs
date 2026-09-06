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
    // Steps with no dependency on `build` and no shared build/restore state - safe to start
    // immediately, in parallel with the rest of the pipeline. Deliberately just `codeguard` for
    // now: test/coverage/sonar/stryker all self-restore/self-build against the same shared
    // obj/bin under RepoRoot, so running any two of *those* concurrently risks MSBuild file-lock
    // collisions until each gets an isolated build (see CLAUDE.md's "Executors self-restore/
    // self-build" limitation).
    private static readonly IReadOnlySet<string> StepsRunIndependently =
        new HashSet<string> { ValidationSteps.CodeGuard };

    public async Task<GateResult> RunAsync(
        ToolContext context,
        IReadOnlyList<string> steps,
        QualityProfile quality,
        CancellationToken cancellationToken)
    {
        // Keep only steps with a registered executor, remembering each one's original
        // (profile-declared) position - completion order no longer matches declaration order
        // once independent steps run concurrently, but reporting should still look deterministic.
        var scheduledSteps = new List<(int Index, string Step, IToolExecutor Executor)>();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!executors.TryGetValue(step, out var executor))
            {
                logger.LogWarning("No executor registered for validation step '{Step}' - skipping", step);
                continue;
            }

            scheduledSteps.Add((i, step, executor));
        }

        var slots = new ToolResult?[steps.Count];

        // Start independent steps immediately, without awaiting them yet, so they overlap with
        // the sequential build/test/coverage/sonar/stryker chain below.
        var independentRuns = new List<(int Index, string Step, Task<ToolResult> Task)>();
        foreach (var (index, step, executor) in scheduledSteps)
        {
            if (StepsRunIndependently.Contains(step))
            {
                logger.LogInformation("Running step '{Step}' (independent of build)", step);
                independentRuns.Add((index, step, executor.ExecuteAsync(context, cancellationToken)));
            }
        }

        foreach (var (index, step, executor) in scheduledSteps)
        {
            if (StepsRunIndependently.Contains(step))
            {
                continue; // already started above; collected after this loop
            }

            logger.LogInformation("Running step '{Step}'", step);
            var result = await executor.ExecuteAsync(context, cancellationToken);
            slots[index] = result;
            logger.LogInformation("Step '{Step}' finished: {Status} ({FindingCount} finding(s))", step, result.Status, result.Findings.Count);

            // There's no value running tests/coverage/CodeGuard against code that doesn't
            // compile (§6.1) - stop here and let the policy evaluate what we do have.
            if (step == ValidationSteps.Build && result.Status != ValidationStatus.Passed)
            {
                logger.LogWarning("Build failed - short-circuiting the remaining validation steps");
                break;
            }
        }

        // Independent steps always run to completion and always contribute a result, regardless
        // of whether `build` (or anything else) failed - that's the whole point of running them
        // in parallel rather than gating them on the rest of the pipeline.
        foreach (var (index, step, task) in independentRuns)
        {
            var result = await task;
            slots[index] = result;
            logger.LogInformation("Step '{Step}' finished: {Status} ({FindingCount} finding(s))", step, result.Status, result.Findings.Count);
        }

        var results = slots.Where(result => result is not null).Select(result => result!).ToList();
        return policyEvaluator.Evaluate(results, quality);
    }
}
