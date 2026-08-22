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
    public async Task RunAsync_RunsEveryStepInOrder_AndReturnsPassedGate_WhenAllPass()
    {
        var build = FakeToolExecutor.Passing(ValidationSteps.Build);
        var test = FakeToolExecutor.Passing(ValidationSteps.Test);
        var engine = CreateEngine(build, test);

        var gate = await engine.RunAsync(Context, [ValidationSteps.Build, ValidationSteps.Test], DefaultQuality, CancellationToken.None);

        Assert.Equal(ValidationStatus.Passed, gate.Status);
        Assert.Equal(2, gate.Tools.Count);
        Assert.Equal(1, build.InvocationCount);
        Assert.Equal(1, test.InvocationCount);
    }

    [Fact]
    public async Task RunAsync_ShortCircuits_WhenBuildFails()
    {
        var build = FakeToolExecutor.Failing(ValidationSteps.Build);
        var test = FakeToolExecutor.Passing(ValidationSteps.Test);
        var codeGuard = FakeToolExecutor.Passing(ValidationSteps.CodeGuard);
        var engine = CreateEngine(build, test, codeGuard);

        var gate = await engine.RunAsync(Context, [ValidationSteps.Build, ValidationSteps.Test, ValidationSteps.CodeGuard], DefaultQuality, CancellationToken.None);

        Assert.Equal(ValidationStatus.Failed, gate.Status);
        Assert.Single(gate.Tools);
        Assert.Equal(0, test.InvocationCount);
        Assert.Equal(0, codeGuard.InvocationCount);
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
