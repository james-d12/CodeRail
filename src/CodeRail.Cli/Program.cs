using System.CommandLine;
using CodeRail.Cli.Commands;
using CodeRail.Cli.Support;
using Microsoft.Extensions.Logging;

using var bootstrapLoggerFactory = CliLoggerFactory.Create(LogLevel.Information);
var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("CodeRail.Cli.Program");

var rootCommand = new RootCommand(
    "CodeRail - deterministic quality-validation and orchestration layer for AI-assisted software development");

rootCommand.Subcommands.Add(ValidateCommand.Build());

try
{
    // System.CommandLine's own default exception handler (EnableDefaultExceptionHandler, true by
    // default) would otherwise catch exceptions from command actions itself, print its own raw
    // "Unhandled exception: " + stack trace, and return 1 without rethrowing - making this catch
    // block dead code. Disabling it here lets exceptions actually reach the friendlier handling
    // below (defense-in-depth for anything not already caught inside a command's own action).
    var invocationConfiguration = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
    return await rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
}
catch (OperationCanceledException)
{
    // User-initiated (Ctrl+C/SIGINT/SIGTERM) - not a bug, so this deliberately doesn't go through
    // the LogCritical/"unexpected error" path below. 130 is the POSIX convention (128 + SIGINT).
    bootstrapLogger.LogInformation("coderail canceled");
    await Console.Error.WriteLineAsync("coderail: canceled.");
    return 130;
}
catch (Exception ex)
{
    bootstrapLogger.LogCritical(ex, "coderail terminated unexpectedly: {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
    await Console.Error.WriteLineAsync($"coderail: unexpected error: {ex.Message}");
    return 1;
}
