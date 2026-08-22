using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Execution.Tests;

public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);
    private readonly string _cwd = Directory.GetCurrentDirectory();

    [Fact]
    public async Task RunAsync_CapturesStandardOutputAndExitCode()
    {
        var result = await _runner.RunAsync("bash", "-c \"echo hello\"", _cwd);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task RunAsync_CapturesStandardErrorAndNonZeroExitCode()
    {
        var result = await _runner.RunAsync("bash", "-c \"echo oops 1>&2; exit 3\"", _cwd);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("oops", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_SetsEnvironmentVariablesOnChildProcess()
    {
        var env = new Dictionary<string, string> { ["CODERAIL_TEST_VAR"] = "bar" };

        var result = await _runner.RunAsync("bash", "-c \"echo $CODERAIL_TEST_VAR\"", _cwd, environmentVariables: env);

        Assert.Contains("bar", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_DisablesMsBuildNodeReuseByDefault()
    {
        // Confirmed by direct reproduction: a `dotnet build`/`dotnet test` spawned as a child of
        // a VSTest testhost process (exactly what CodeRail.IntegrationTests does) hangs
        // indefinitely negotiating with a reusable MSBuild node, while the identical command
        // completes in seconds with node reuse disabled. This must be on by default for every
        // spawned process, not just build/test executors specifically.
        var result = await _runner.RunAsync("bash", "-c \"echo [$MSBUILDDISABLENODEREUSE]\"", _cwd);

        Assert.Contains("[1]", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_CallerSuppliedEnvironmentVariable_OverridesTheDefault()
    {
        var env = new Dictionary<string, string> { ["MSBUILDDISABLENODEREUSE"] = "0" };

        var result = await _runner.RunAsync("bash", "-c \"echo [$MSBUILDDISABLENODEREUSE]\"", _cwd, environmentVariables: env);

        Assert.Contains("[0]", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_MeasuresWallClockDuration()
    {
        var result = await _runner.RunAsync("bash", "-c \"sleep 0.2\"", _cwd);

        Assert.True(result.Duration >= TimeSpan.FromMilliseconds(150), $"Expected duration >= 150ms, was {result.Duration}");
    }

    [Fact]
    public async Task RunAsync_ReportsTimedOut_WhenProcessExceedsTimeout()
    {
        var result = await _runner.RunAsync(
            "bash", "-c \"sleep 10\"", _cwd, timeout: TimeSpan.FromMilliseconds(200));

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.True(result.Duration < TimeSpan.FromSeconds(5), "Timed-out process should be killed well before its own sleep completes");
    }

    [Fact]
    public async Task RunAsync_ThrowsOperationCanceledException_WhenCallerCancels()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _runner.RunAsync("bash", "-c \"sleep 10\"", _cwd, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RunAsync_TruncatesOutputBeyondCap()
    {
        // A single 2,000,000-character line, comfortably over the 1,000,000-char cap.
        var result = await _runner.RunAsync("bash", "-c \"printf '%02000000d' 1\"", _cwd);

        Assert.True(result.StandardOutput.Length < 1_100_000, $"Expected capped output, got {result.StandardOutput.Length} chars");
        Assert.Contains("[output truncated]", result.StandardOutput);
    }

    [Theory]
    [InlineData("CORECLR_ENABLE_PROFILING")]
    [InlineData("CORECLR_PROFILER")]
    [InlineData("CORECLR_PROFILER_PATH")]
    [InlineData("COR_ENABLE_PROFILING")]
    [InlineData("COR_PROFILER")]
    [InlineData("VSTEST_HOST_DEBUG")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    public async Task RunAsync_StripsClrProfilerAndDiagnosticsVariables_InheritedFromThisProcess(string variableName)
    {
        // Reproduces a real hang: when CodeRail's own test suite runs under `dotnet test`
        // (coverlet.collector loaded as a test adapter), these variables get inherited by
        // spawned `dotnet build/test` child processes, and a child that itself tries to
        // instrument a target assembly's coverage hangs indefinitely on a profiler
        // pipe/session collision instead of completing in seconds.
        Environment.SetEnvironmentVariable(variableName, "leaked-from-host-process");
        try
        {
            var result = await _runner.RunAsync("bash", $"-c \"echo [${variableName}]\"", _cwd);

            Assert.Contains("[]", result.StandardOutput);
            Assert.DoesNotContain("leaked-from-host-process", result.StandardOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }
}
