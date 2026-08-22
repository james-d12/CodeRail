using System.CommandLine;
using CodeRail.Cli.Support;
using CodeRail.Configuration;
using CodeRail.Engine;
using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Policy;
using CodeRail.Reporting;
using CodeRail.Reporting.Console;
using CodeRail.Reporting.Json;
using CodeRail.Reporting.Sarif;
using CodeRail.Tooling;
using CodeRail.Tooling.Executors;
using Microsoft.Extensions.Logging;

namespace CodeRail.Cli.Commands;

public static class ValidateCommand
{
    public static Command Build()
    {
        var pathOption = CommonOptions.CreatePathOption();
        var profileOption = CommonOptions.CreateProfileOption();
        var verbosityOption = CommonOptions.CreateVerbosityOption();

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: console, json, or sarif.",
            DefaultValueFactory = _ => "console"
        };
        formatOption.AcceptOnlyFromAmong("console", "json", "sarif");

        var outputOption = new Option<string?>("--output")
        {
            Description = "File to write the report to (default: stdout)."
        };

        var baseRefOption = new Option<string?>("--base-ref")
        {
            Description = "Git ref to diff against for changed-code-aware thresholds (docs §12), " +
                "e.g. 'origin/main'. Default: changed-code awareness is off - only whole-repository " +
                "thresholds are evaluated."
        };

        var command = new Command("validate", "Run the validation pipeline and report a pass/fail quality gate");
        command.Add(pathOption);
        command.Add(profileOption);
        command.Add(formatOption);
        command.Add(outputOption);
        command.Add(baseRefOption);
        command.Add(verbosityOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using var loggerFactory = CliLoggerFactory.Create(CliLoggerFactory.ParseVerbosity(parseResult.GetValue(verbosityOption)!));
            var logger = loggerFactory.CreateLogger(typeof(ValidateCommand));

            var repoRoot = Path.GetFullPath(parseResult.GetValue(pathOption) ?? Directory.GetCurrentDirectory());
            if (!Directory.Exists(repoRoot))
            {
                logger.LogError("Path '{RepoRoot}' does not exist", repoRoot);
                await Console.Error.WriteLineAsync($"coderail: path '{repoRoot}' does not exist.");
                return 1;
            }

            try
            {
                var profilePath = parseResult.GetValue(profileOption);
                var profile = profilePath is null
                    ? ValidationProfileLoader.LoadDefault()
                    : ValidationProfileLoader.LoadFromFile(profilePath);

                var processRunner = new ProcessRunner(loggerFactory.CreateLogger<ProcessRunner>());

                var solutionPaths = SolutionFileLocator.Resolve(repoRoot, [], loggerFactory.CreateLogger(typeof(SolutionFileLocator)));

                ChangeSet? changes = null;
                var baseRef = parseResult.GetValue(baseRefOption);
                if (baseRef is not null)
                {
                    changes = await GitChangeResolver.ResolveAsync(
                        processRunner, repoRoot, baseRef, loggerFactory.CreateLogger(typeof(GitChangeResolver)), cancellationToken);
                    logger.LogInformation(
                        "Changed-code awareness enabled against '{BaseRef}': {FileCount} changed file(s)", baseRef, changes.ChangedFiles.Count);
                }

                var sonarConfig = profile.Quality.Sonar.ProjectKey is { } projectKey
                    ? new SonarConfig(projectKey, profile.Quality.Sonar.Organization, profile.Quality.Sonar.HostUrl)
                    : null;

                var context = new ToolContext(repoRoot, solutionPaths, changes, sonarConfig);

                using var httpClient = new HttpClient();
                var engine = BuildEngine(loggerFactory, processRunner, httpClient);
                var gate = await engine.RunAsync(context, profile.Validation, profile.Quality, cancellationToken);

                var writer = CreateWriter(parseResult.GetValue(formatOption)!);
                var outputPath = parseResult.GetValue(outputOption);

                TextWriter destination = outputPath is null ? Console.Out : new StreamWriter(outputPath);
                try
                {
                    await writer.WriteAsync(gate, destination, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }
                finally
                {
                    if (outputPath is not null)
                    {
                        await destination.DisposeAsync();
                    }
                }

                // This exit code IS the AI repair-loop contract (docs §16) - it must be reliable.
                return gate.Status == ValidationStatus.Passed ? 0 : 1;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Validation failed: {Message}", ex.Message);
                await Console.Error.WriteLineAsync($"coderail: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static ValidationEngine BuildEngine(ILoggerFactory loggerFactory, IProcessRunner processRunner, HttpClient httpClient)
    {
        var executors = new IToolExecutor[]
        {
            new DotnetBuildExecutor(processRunner, loggerFactory.CreateLogger<DotnetBuildExecutor>()),
            new DotnetTestExecutor(processRunner, loggerFactory.CreateLogger<DotnetTestExecutor>()),
            new CodeGuardExecutor(processRunner, loggerFactory.CreateLogger<CodeGuardExecutor>()),
            new CoverageExecutor(processRunner, loggerFactory.CreateLogger<CoverageExecutor>()),
            new StrykerExecutor(processRunner, loggerFactory.CreateLogger<StrykerExecutor>()),
            new SonarExecutor(processRunner, httpClient, loggerFactory.CreateLogger<SonarExecutor>())
        }.ToDictionary(e => e.Name, e => e);

        return new ValidationEngine(executors, new PolicyEvaluator(), loggerFactory.CreateLogger<ValidationEngine>());
    }

    private static IGateResultWriter CreateWriter(string format) => format switch
    {
        "json" => new JsonGateResultWriter(),
        "sarif" => new SarifGateResultWriter(),
        _ => new ConsoleGateResultWriter()
    };
}
