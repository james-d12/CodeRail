namespace CodeRail.Execution;

/// <summary>
/// Thin abstraction over starting an external process. Every tool executor goes through this -
/// nothing calls <see cref="System.Diagnostics.Process"/> directly - so process handling
/// (cancellation, timeouts, output capture) lives in exactly one place.
/// See <c>docs/HIGH_LEVEL_PLAN.md</c> §8 (Process Execution).
/// </summary>
public interface IProcessRunner
{
    /// <param name="executable">The executable to run, e.g. "dotnet".</param>
    /// <param name="arguments">The raw argument string, e.g. "build --no-restore".</param>
    /// <param name="workingDirectory">Directory the process is started in.</param>
    /// <param name="environmentVariables">Extra environment variables to set on the child
    /// process, on top of whatever this process already has. Null means "inherit only".</param>
    /// <param name="timeout">Maximum time to allow the process to run before it is killed and
    /// <see cref="ProcessResult.TimedOut"/> is reported as true. Null means no timeout.</param>
    /// <param name="cancellationToken">External cancellation (e.g. Ctrl+C) - unlike a timeout,
    /// this throws <see cref="OperationCanceledException"/> rather than returning a result,
    /// matching standard .NET cancellation conventions.</param>
    Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of running an external process.</summary>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut);
