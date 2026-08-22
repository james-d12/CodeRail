namespace CodeRail.Configuration.Tests;

public class ValidationProfileLoaderTests
{
    [Fact]
    public void LoadDefault_ReturnsTheBuiltInDotnetDefaultProfile()
    {
        var profile = ValidationProfileLoader.LoadDefault();

        Assert.Equal("dotnet-default", profile.Profile);
        Assert.Equal(["build", "test", "codeguard", "coverage"], profile.Validation);
        Assert.Equal(0, profile.Quality.Test.AllowFailures);
        Assert.Equal(0, profile.Quality.CodeGuard.ErrorCount);
        Assert.Equal(0, profile.Quality.CodeGuard.CriticalCount);
        Assert.Null(profile.Quality.Coverage.Minimum);
    }

    [Fact]
    public void LoadFromFile_ParsesACustomProfileIncludingCoverageThreshold()
    {
        const string yaml = """
            profile: strict
            validation:
              - build
              - test
              - coverage
            quality:
              test:
                allowFailures: 1
              coverage:
                minimum: 85.5
            """;
        var path = WriteTempYaml(yaml);

        var profile = ValidationProfileLoader.LoadFromFile(path);

        Assert.Equal("strict", profile.Profile);
        Assert.Equal(["build", "test", "coverage"], profile.Validation);
        Assert.Equal(1, profile.Quality.Test.AllowFailures);
        Assert.Equal(85.5, profile.Quality.Coverage.Minimum);
    }

    [Fact]
    public void LoadFromFile_IgnoresUnrecognizedTopLevelKeys()
    {
        const string yaml = """
            profile: dotnet-default
            validation:
              - build
            someFutureKeyThisVersionDoesNotKnowAbout: true
            """;
        var path = WriteTempYaml(yaml);

        var profile = ValidationProfileLoader.LoadFromFile(path);

        Assert.Equal("dotnet-default", profile.Profile);
    }

    [Fact]
    public void LoadFromFile_Throws_WhenFileDoesNotExist()
    {
        Assert.Throws<FileNotFoundException>(() => ValidationProfileLoader.LoadFromFile(Path.Combine(Path.GetTempPath(), "does-not-exist.yml")));
    }

    private static string WriteTempYaml(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coderail-profile-{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, content);
        return path;
    }
}
