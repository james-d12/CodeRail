using CodeRail.Cli.Commands;

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
