using CodeRail.Execution;

namespace CodeRail.Tooling.Tests.Executors;

/// <summary>A scripted <see cref="IProcessRunner"/> for exercising executors without spawning
/// real processes (or requiring tools like `codeguard` to actually be installed).</summary>
internal sealed class StubProcessRunner(Func<string, string, string, ProcessResult> handler) : IProcessRunner
{
    public static StubProcessRunner Returning(ProcessResult result) => new((_, _, _) => result);

    public static StubProcessRunner Throwing(Exception exception) => new((_, _, _) => throw exception);

    public List<(string Executable, string Arguments, string WorkingDirectory)> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((executable, arguments, workingDirectory));
        return Task.FromResult(handler(executable, arguments, workingDirectory));
    }
}
