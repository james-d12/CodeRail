namespace CodeRail.Cli.Tests;

/// <summary>
/// Tests that redirect the process-global <see cref="Console.Out"/>/<see cref="Console.Error"/>
/// to capture command output must share this collection so xUnit runs them sequentially -
/// different test classes are otherwise separate collections run in parallel by default, which
/// races on the shared static Console state.
/// </summary>
[CollectionDefinition(Name)]
public class ConsoleOutputCollection
{
    public const string Name = "Console output";
}
