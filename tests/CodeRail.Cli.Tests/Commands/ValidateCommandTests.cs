using CodeRail.Cli.Commands;
using CodeRail.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Cli.Tests.Commands;

[Collection(ConsoleOutputCollection.Name)]
public class ValidateCommandTests
{
    [Fact]
    public void Build_ReturnsValidateCommand_WithExpectedOptions()
    {
        var command = ValidateCommand.Build();

        Assert.Equal("validate", command.Name);
        var optionNames = command.Options.Select(o => o.Name).ToHashSet();
        Assert.Contains("--path", optionNames);
        Assert.Contains("--profile", optionNames);
        Assert.Contains("--format", optionNames);
        Assert.Contains("--output", optionNames);
        Assert.Contains("--base-ref", optionNames);
        Assert.Contains("--verbosity", optionNames);
    }

    [Fact]
    public async Task Invoke_WithNonexistentPath_ExitsOneAndReportsTheMissingPath()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"coderail-does-not-exist-{Guid.NewGuid():N}");

        var (exitCode, _, error) = await RunAsync(["--path", missingPath]);

        Assert.Equal(1, exitCode);
        Assert.Contains(missingPath, error);
        Assert.Contains("does not exist", error);
    }

    [Fact]
    public async Task Invoke_WithInvalidFormatValue_FailsParsingBeforeRunningAnything()
    {
        var (exitCode, _, _) = await RunAsync(["--format", "yaml"]);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Invoke_AgainstDirectoryWithNoBuildableProject_StillProducesAReport_InsteadOfCrashing()
    {
        var emptyDir = Directory.CreateTempSubdirectory("coderail-cli-empty-").FullName;
        try
        {
            var (exitCode, output, _) = await RunAsync(["--path", emptyDir, "--format", "json", "--verbosity", "error"]);

            Assert.Equal(1, exitCode);
            Assert.Contains("\"status\": \"failed\"", output);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public async Task Invoke_WithBaseRefAgainstADirectoryThatIsNotAGitRepository_ExitsOneWithAnActionableError()
    {
        var nonGitDir = Directory.CreateTempSubdirectory("coderail-cli-not-git-").FullName;
        try
        {
            var (exitCode, _, error) = await RunAsync(["--path", nonGitDir, "--base-ref", "HEAD", "--verbosity", "error"]);

            Assert.Equal(1, exitCode);
            Assert.Contains("coderail:", error);
        }
        finally
        {
            Directory.Delete(nonGitDir, recursive: true);
        }
    }

    [Fact]
    public async Task Invoke_WithBaseRef_ResolvesChangedFiles_AgainstARealGitRepository()
    {
        var repoDir = Directory.CreateTempSubdirectory("coderail-cli-git-").FullName;
        var emptyStepsProfile = Path.Combine(repoDir, "no-steps.yml");
        try
        {
            await RunGit(repoDir, "init");
            await RunGit(repoDir, "config user.email test@example.com");
            await RunGit(repoDir, "config user.name \"CodeRail Test\"");
            File.WriteAllText(Path.Combine(repoDir, "Existing.cs"), "// v1\n");
            await RunGit(repoDir, "add Existing.cs");
            await RunGit(repoDir, "commit -m initial");
            File.WriteAllText(emptyStepsProfile, "profile: no-steps\nvalidation: []\nquality: {}\n");

            // A change no `validation:` step exists to evaluate - this only exercises that
            // --base-ref resolution itself doesn't blow up the pipeline, not any executor.
            File.WriteAllText(Path.Combine(repoDir, "Existing.cs"), "// v2\n");

            var (exitCode, output, _) = await RunAsync(
                ["--path", repoDir, "--profile", emptyStepsProfile, "--base-ref", "HEAD", "--format", "json", "--verbosity", "error"]);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"status\": \"passed\"", output);
        }
        finally
        {
            Directory.Delete(repoDir, recursive: true);
        }
    }

    private static async Task RunGit(string workingDirectory, string arguments)
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var result = await runner.RunAsync("git", arguments, workingDirectory, timeout: TimeSpan.FromSeconds(30));
        Assert.True(result.ExitCode == 0, $"'git {arguments}' failed: {result.StandardError}");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var outWriter = new StringWriter();
        var errorWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errorWriter);
        try
        {
            var exitCode = await ValidateCommand.Build().Parse(args).InvokeAsync();
            return (exitCode, outWriter.ToString(), errorWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
