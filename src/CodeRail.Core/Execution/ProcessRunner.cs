using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CodeRail.Execution;

/// <inheritdoc cref="IProcessRunner"/>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    /// <summary>Per-stream cap on captured output, so a runaway tool can't exhaust memory.
    /// See <c>docs/HIGH_LEVEL_PLAN.md</c> §21 ("capture and restrict excessive process output").</summary>
    private const int MaxCapturedOutputChars = 1_000_000;

    /// <summary>
    /// .NET CLR profiler/diagnostics variables that must never be inherited by a spawned
    /// <c>dotnet</c> child process - see §21 ("never expose environment variables
    /// indiscriminately"). Defensive: prevents a profiled/instrumented host process (e.g.
    /// CodeRail itself running under a coverage-collecting `dotnet test`) from leaking profiler
    /// attach hooks into a child `dotnet build`/`dotnet test` invocation.
    /// </summary>
    private static readonly string[] EnvironmentVariablesToStrip =
    [
        "CORECLR_ENABLE_PROFILING", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH",
        "CORECLR_PROFILER_PATH_32", "CORECLR_PROFILER_PATH_64",
        "COR_ENABLE_PROFILING", "COR_PROFILER", "COR_PROFILER_PATH",
        "VSTEST_HOST_DEBUG", "VSTEST_RUNNER_DEBUG_ATTACHVS", "DOTNET_STARTUP_HOOKS"
    ];

    /// <summary>
    /// Every spawned process gets this by default (a caller-supplied value for the same key
    /// still wins - see the merge order in <see cref="RunAsync"/>). Disables MSBuild's
    /// node-reuse feature (persistent `dotnet exec MSBuild.dll /nodeReuse:true` worker
    /// processes). CodeRail runs one-shot validation invocations, not an interactive dev loop, so
    /// there's no steady-state benefit to reuse - and it's actively dangerous here: confirmed by
    /// direct reproduction, a `dotnet build`/`dotnet test` spawned as a child of a VSTest
    /// testhost process (i.e. exactly what CodeRail.IntegrationTests does, and what happens for
    /// real whenever an agent runs CodeRail's own test suite) hangs indefinitely - not merely
    /// slow - trying to negotiate with a node-reuse worker, while the identical command run from
    /// an ordinary shell or `dotnet run` completes in a few seconds. Multi-project solutions seem
    /// most exposed; single-project builds were not observed to hang, but the risk isn't worth
    /// re-deriving per call site.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DefaultEnvironmentVariables =
        new Dictionary<string, string> { ["MSBUILDDISABLENODEREUSE"] = "1" };

    public async Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var name in EnvironmentVariablesToStrip)
        {
            startInfo.Environment.Remove(name);
        }

        foreach (var (key, value) in DefaultEnvironmentVariables)
        {
            startInfo.Environment[key] = value;
        }

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdOut = new CappedStringBuilder(MaxCapturedOutputChars);
        var stdErr = new CappedStringBuilder(MaxCapturedOutputChars);
        process.OutputDataReceived += (_, e) => stdOut.AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => stdErr.AppendLine(e.Data);

        logger.LogDebug("Running: {Executable} {Arguments} (cwd: {WorkingDirectory})", executable, arguments, workingDirectory);

        using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : null;
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var waitToken = linkedCts?.Token ?? cancellationToken;

        var stopwatch = Stopwatch.StartNew();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(waitToken);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            // The timeout fired, not the caller's own cancellation - this is a normal, reportable
            // outcome (the tool is treated as failed), not an exception the caller has to handle.
            timedOut = true;
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Caller-initiated cancellation (e.g. Ctrl+C) - kill the tree so nothing is left
            // running, then let the cancellation propagate per standard .NET conventions.
            KillProcessTree(process);
            throw;
        }

        stopwatch.Stop();

        logger.LogDebug(
            "Finished: {Executable} (exit code: {ExitCode}, duration: {DurationMs}ms, timed out: {TimedOut})",
            executable, timedOut ? -1 : process.ExitCode, stopwatch.ElapsedMilliseconds, timedOut);

        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            stdOut.ToString(),
            stdErr.ToString(),
            stopwatch.Elapsed,
            timedOut);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the HasExited check and Kill - nothing to do.
        }
    }

    /// <summary>Accumulates process output lines up to a fixed character budget, then silently
    /// drops the rest rather than growing without bound.</summary>
    private sealed class CappedStringBuilder(int maxChars)
    {
        private readonly StringBuilder _builder = new();
        private bool _truncated;

        public void AppendLine(string? line)
        {
            if (line is null || _truncated)
            {
                return;
            }

            if (_builder.Length + line.Length > maxChars)
            {
                _builder.Append("... [output truncated]");
                _truncated = true;
                return;
            }

            _builder.AppendLine(line);
        }

        public override string ToString() => _builder.ToString();
    }
}
