using CodeRail.Cli.Support;
using Microsoft.Extensions.Logging;

namespace CodeRail.Cli.Tests;

public class CliLoggerFactoryTests
{
    [Theory]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("information", LogLevel.Information)]
    [InlineData("WARNING", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("critical", LogLevel.Critical)]
    public void ParseVerbosity_AcceptsKnownLevels_CaseInsensitively(string value, LogLevel expected)
    {
        Assert.Equal(expected, CliLoggerFactory.ParseVerbosity(value));
    }

    [Theory]
    [InlineData("trace")]
    [InlineData("none")]
    [InlineData("not-a-level")]
    [InlineData("")]
    public void ParseVerbosity_Throws_ForUnsupportedOrInvalidValues(string value)
    {
        Assert.Throws<FormatException>(() => CliLoggerFactory.ParseVerbosity(value));
    }

    [Fact]
    public void Create_ReturnsAWorkingLoggerFactory()
    {
        using var factory = CliLoggerFactory.Create(LogLevel.Information);
        var logger = factory.CreateLogger("test");

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
    }
}
