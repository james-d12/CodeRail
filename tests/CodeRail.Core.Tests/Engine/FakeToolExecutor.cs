using CodeRail.Evidence;
using CodeRail.Tooling;

namespace CodeRail.Engine.Tests;

/// <summary>A scripted <see cref="IToolExecutor"/> for exercising <see cref="ValidationEngine"/>
/// without spawning real processes.</summary>
internal sealed class FakeToolExecutor(string name, Task<ToolResult> result) : IToolExecutor
{
    public int InvocationCount { get; private set; }
    public CancellationToken? LastCancellationToken { get; private set; }

    public string Name => name;

    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        InvocationCount++;
        LastCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static FakeToolExecutor Passing(string name) =>
        new(name, Task.FromResult(PassingResult(name)));

    public static FakeToolExecutor Failing(string name) =>
        new(name, Task.FromResult(new ToolResult(
            name, ValidationStatus.Failed,
            [new Finding("failure", Severity.Error, $"{name} failed", null, null, null)],
            new Dictionary<string, object>(), [], TimeSpan.Zero)));

    /// <summary>An executor whose result doesn't become available until <paramref name="pendingResult"/>
    /// completes - used to prove that another step doesn't wait on this one.</summary>
    public static FakeToolExecutor Blocking(string name, Task<ToolResult> pendingResult) =>
        new(name, pendingResult);

    public static ToolResult PassingResult(string name) =>
        new(name, ValidationStatus.Passed, [], new Dictionary<string, object>(), [], TimeSpan.Zero);
}
