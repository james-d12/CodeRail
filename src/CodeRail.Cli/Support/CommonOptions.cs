using System.CommandLine;

namespace CodeRail.Cli.Support;

/// <summary>Options shared by every command that resolves a repository to validate.</summary>
public static class CommonOptions
{
    public static Option<string?> CreatePathOption() => new("--path")
    {
        Description = "Repository root to validate (default: current directory)."
    };

    public static Option<string?> CreateProfileOption() => new("--profile")
    {
        Description = "Path to a validation profile YAML file (default: the built-in dotnet-default profile)."
    };

    public static Option<string> CreateVerbosityOption()
    {
        var option = new Option<string>("--verbosity")
        {
            Description = "Minimum log level written to stderr: debug, information, warning, error, or " +
                "critical (case-insensitive). Default: information.",
            DefaultValueFactory = _ => "information"
        };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>() ?? "information";
            try
            {
                CliLoggerFactory.ParseVerbosity(value);
            }
            catch (FormatException ex)
            {
                result.AddError(ex.Message);
            }
        });
        return option;
    }
}
