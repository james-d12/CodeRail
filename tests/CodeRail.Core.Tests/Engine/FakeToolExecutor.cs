using CodeRail.Evidence;
using CodeRail.Tooling;

namespace CodeRail.Engine.Tests;

/// <summary>A scripted <see cref="IToolExecutor"/> for exercising <see cref="ValidationEngine"/>
/// without spawning real processes.</summary>
internal sealed class FakeToolExecutor(string name, ToolResult result) : IToolExecutor
{
    public int InvocationCount { get; private set; }
    public CancellationToken? LastCancellationToken { get; private set; }

    public string Name => name;

    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        InvocationCount++;
        LastCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }

    public static FakeToolExecutor Passing(string name) =>
        new(name, new ToolResult(name, ValidationStatus.Passed, [], new Dictionary<string, object>(), [], TimeSpan.Zero));

    public static FakeToolExecutor Failing(string name) =>
        new(name, new ToolResult(
            name, ValidationStatus.Failed,
            [new Finding("failure", Severity.Error, $"{name} failed", null, null, null)],
            new Dictionary<string, object>(), [], TimeSpan.Zero));
}
