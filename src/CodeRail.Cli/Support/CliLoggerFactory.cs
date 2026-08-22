using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace CodeRail.Cli.Support;

/// <summary>
/// Builds one <see cref="ILoggerFactory"/> per command invocation from the parsed --verbosity
/// value. All output goes to stderr unconditionally so stdout stays clean for
/// `validate --format json` machine-readable output. Ported from CodeGuard's
/// <c>CliLoggerFactory</c>.
/// </summary>
public static class CliLoggerFactory
{
    public static ILoggerFactory Create(LogLevel minimumLevel) =>
        LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimumLevel);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
            });
            builder.Services.Configure<ConsoleLoggerOptions>(options =>
                options.LogToStandardErrorThreshold = LogLevel.Trace);
        });

    /// <summary>
    /// Parses a --verbosity value (case-insensitive) into a <see cref="LogLevel"/>. Only
    /// debug/information/warning/error/critical are accepted - Enum.TryParse alone would also
    /// accept LogLevel's "trace" and "none" members, which are deliberately excluded here.
    /// </summary>
    public static LogLevel ParseVerbosity(string value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level)
            && level is LogLevel.Debug or LogLevel.Information or LogLevel.Warning or LogLevel.Error or LogLevel.Critical
            ? level
            : throw new FormatException(
                $"Invalid --verbosity value '{value}'. Must be one of: debug, information, warning, error, critical.");
}
