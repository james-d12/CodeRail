using CodeRail.Cli.Commands;

namespace CodeRail.IntegrationTests;

/// <summary>
/// Exercises `coderail validate` end-to-end - real <c>dotnet build</c>/<c>dotnet test</c>/coverage
/// collection via <c>ProcessRunner</c>, through the real <c>ValidationEngine</c> and
/// <c>PolicyEvaluator</c>, reported as real JSON - against small fixture "target repos" under
/// <c>Fixtures/</c>. Every scenario uses a profile that omits the "codeguard" step (see
/// <c>Profiles/build-test-only.yml</c>) so these tests stay hermetic regardless of whatever
/// codeguard installation happens to exist on the machine running them.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public class ValidateEndToEndTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string ProfilesRoot = Path.Combine(AppContext.BaseDirectory, "Profiles");

    [Fact]
    public async Task Validate_AgainstAHealthyProject_PassesTheGate()
    {
        var (exitCode, output) = await RunAsync("AllPass", "no-codeguard.yml");

        Assert.Equal(0, exitCode);
        Assert.Contains("\"status\": \"passed\"", output);
        Assert.Contains("\"tool\": \"dotnet-build\"", output);
        Assert.Contains("\"tool\": \"dotnet-test\"", output);
        Assert.Contains("\"tool\": \"coverage\"", output);
        Assert.Contains("\"blockingFindings\": []", output);
    }

    [Fact]
    public async Task Validate_WithSarifFormat_ProducesAValidSarifDocument()
    {
        var (exitCode, output) = await RunAsync("AllPass", "no-codeguard.yml", format: "sarif");

        Assert.Equal(0, exitCode);
        Assert.Contains("\"$schema\":", output);
        Assert.Contains("\"version\": \"2.1.0\"", output);
        Assert.Contains("\"CodeRail\"", output);
    }

    [Fact]
    public async Task Validate_AgainstABrokenBuild_ShortCircuits_AndNeverRunsTest()
    {
        var (exitCode, output) = await RunAsync("BuildFailure", "build-test-only.yml");

        Assert.Equal(1, exitCode);
        Assert.Contains("\"status\": \"failed\"", output);
        Assert.Contains("\"tool\": \"dotnet-build\"", output);
        Assert.DoesNotContain("\"tool\": \"dotnet-test\"", output);
    }

    [Fact]
    public async Task Validate_AgainstFailingTests_FailsTheGate_WithATestFailureFinding()
    {
        var (exitCode, output) = await RunAsync("TestFailure", "build-test-only.yml");

        Assert.Equal(1, exitCode);
        Assert.Contains("\"status\": \"failed\"", output);
        Assert.Contains("\"type\": \"test-failure\"", output);
        Assert.Contains("AlwaysFails", output);
    }

    [Fact]
    public async Task Validate_AgainstLowCoverage_FailsTheGate_WithACoverageBelowThresholdFinding()
    {
        var (exitCode, output) = await RunAsync("LowCoverage", "strict-coverage.yml");

        Assert.Equal(1, exitCode);
        Assert.Contains("\"status\": \"failed\"", output);
        Assert.Contains("\"type\": \"coverage-below-threshold\"", output);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fixtureName, string profileFileName, string format = "json")
    {
        var path = Path.Combine(FixturesRoot, fixtureName);
        var profilePath = Path.Combine(ProfilesRoot, profileFileName);

        var originalOut = Console.Out;
        var originalError = Console.Error;
        var outWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(TextWriter.Null);
        try
        {
            var exitCode = await ValidateCommand.Build()
                .Parse(["--path", path, "--profile", profilePath, "--format", format, "--verbosity", "error"])
                .InvokeAsync();
            return (exitCode, outWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
