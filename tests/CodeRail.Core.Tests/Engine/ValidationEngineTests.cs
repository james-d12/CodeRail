using CodeRail.Evidence;
using CodeRail.Policy;
using CodeRail.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Engine.Tests;

public class ValidationEngineTests
{
    private static readonly ToolContext Context = new("/repo", []);
    private static readonly QualityProfile DefaultQuality = new();

    private static ValidationEngine CreateEngine(params FakeToolExecutor[] executors) =>
        new(
            executors.ToDictionary(e => e.Name, e => (IToolExecutor)e),
            new PolicyEvaluator(),
            NullLogger<ValidationEngine>.Instance);

    [Fact]
    public async Task RunAsync_RunsEveryStepInProfileOrder_AndReturnsPassedGate_WhenAllPass()
    {
        var build = FakeToolExecutor.Passing(ValidationSteps.Build);
        var test = FakeToolExecutor.Passing(ValidationSteps.Test);
        var codeGuard = FakeToolExecutor.Passing(ValidationSteps.CodeGuard);
        var engine = CreateEngine(build, test, codeGuard);

        // `codeguard` is declared first, but it runs independently of build/test - the result
        // order should still follow profile-declared order, not completion order.
        var gate = await engine.RunAsync(
            Context, [ValidationSteps.CodeGuard, ValidationSteps.Build, ValidationSteps.Test], DefaultQuality, CancellationToken.None);

        Assert.Equal(ValidationStatus.Passed, gate.Status);
        Assert.Equal(
            [ValidationSteps.CodeGuard, ValidationSteps.Build, ValidationSteps.Test],
            gate.Tools.Select(tool => tool.Tool));
        Assert.Equal(1, build.InvocationCount);
        Assert.Equal(1, test.InvocationCount);
        Assert.Equal(1, codeGuard.InvocationCount);
    }

    [Fact]
    public async Task RunAsync_ShortCircuits_WhenBuildFails_ButCodeGuardStillRuns()
    {
        var build = FakeToolExecutor.Failing(ValidationSteps.Build);
        var test = FakeToolExecutor.Passing(ValidationSteps.Test);
        var codeGuard = FakeToolExecutor.Passing(ValidationSteps.CodeGuard);
        var engine = CreateEngine(build, test, codeGuard);

        var gate = await engine.RunAsync(Context, [ValidationSteps.Build, ValidationSteps.Test, ValidationSteps.CodeGuard], DefaultQuality, CancellationToken.None);

        Assert.Equal(ValidationStatus.Failed, gate.Status);
        Assert.Equal(2, gate.Tools.Count);
        Assert.Equal(0, test.InvocationCount);
        // codeguard has no dependency on build, so it always runs - even when build fails.
        Assert.Equal(1, codeGuard.InvocationCount);
    }

    [Fact]
    public async Task RunAsync_RunsCodeGuard_WithoutWaitingForBuildToFinish()
    {
        var buildGate = new TaskCompletionSource<ToolResult>();
        var build = FakeToolExecutor.Blocking(ValidationSteps.Build, buildGate.Task);
        var codeGuard = FakeToolExecutor.Passing(ValidationSteps.CodeGuard);
        var engine = CreateEngine(build, codeGuard);

        var runTask = engine.RunAsync(Context, [ValidationSteps.Build, ValidationSteps.CodeGuard], DefaultQuality, CancellationToken.None);

        // By the time RunAsync yields control back here (awaiting build, which is still
        // pending), codeguard has already run to completion - it never waited on build.
        Assert.Equal(1, codeGuard.InvocationCount);
        Assert.False(runTask.IsCompleted);

        buildGate.SetResult(FakeToolExecutor.PassingResult(ValidationSteps.Build));
        var gate = await runTask;

        Assert.Equal(ValidationStatus.Passed, gate.Status);
        Assert.Equal(2, gate.Tools.Count);
    }

    [Fact]
    public async Task RunAsync_DoesNotShortCircuit_WhenANonBuildStepFails()
    {
        var build = FakeToolExecutor.Passing(ValidationSteps.Build);
        var test = FakeToolExecutor.Failing(ValidationSteps.Test);
        var codeGuard = FakeToolExecutor.Passing(ValidationSteps.CodeGuard);
        var engine = CreateEngine(build, test, codeGuard);

        var gate = await engine.RunAsync(Context, [ValidationSteps.Build, ValidationSteps.Test, ValidationSteps.CodeGuard], DefaultQuality, CancellationToken.None);

        Assert.Equal(ValidationStatus.Failed, gate.Status);
        Assert.Equal(3, gate.Tools.Count);
        Assert.Equal(1, codeGuard.InvocationCount);
    }

    [Fact]
    public async Task RunAsync_SkipsSteps_WithNoRegisteredExecutor()
    {
        var build = FakeToolExecutor.Passing(ValidationSteps.Build);
        var engine = CreateEngine(build);

        var gate = await engine.RunAsync(Context, [ValidationSteps.Build, "some-future-step"], DefaultQuality, CancellationToken.None);

        Assert.Equal(ValidationStatus.Passed, gate.Status);
        Assert.Single(gate.Tools);
    }

    [Fact]
    public async Task RunAsync_PropagatesCancellationToken_ToEachExecutor()
    {
        var build = FakeToolExecutor.Passing(ValidationSteps.Build);
        var engine = CreateEngine(build);
        using var cts = new CancellationTokenSource();

        await engine.RunAsync(Context, [ValidationSteps.Build], DefaultQuality, cts.Token);

        Assert.Equal(cts.Token, build.LastCancellationToken);
    }

    [Fact]
    public async Task RunAsync_ReturnsPassedGate_ForAnEmptyStepList()
    {
        var engine = CreateEngine();

        var gate = await engine.RunAsync(Context, [], DefaultQuality, CancellationToken.None);

        Assert.Equal(ValidationStatus.Passed, gate.Status);
        Assert.Empty(gate.Tools);
    }
}
