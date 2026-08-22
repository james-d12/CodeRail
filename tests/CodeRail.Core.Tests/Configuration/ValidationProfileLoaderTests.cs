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

    // The example profiles under examples/profiles/ (docs §13/§14's fast/medium/thorough tiers)
    // are documentation, not compiled code - these are a smoke test against YAML/key-name typos,
    // copied to ExampleProfiles/ in the test output by CodeRail.Core.Tests.csproj.
    [Theory]
    [InlineData("dotnet-fast.yml", "dotnet-fast", new[] { "build", "test", "codeguard" })]
    [InlineData("dotnet-medium.yml", "dotnet-medium", new[] { "build", "test", "codeguard", "coverage", "sonar" })]
    [InlineData("dotnet-thorough.yml", "dotnet-thorough", new[] { "build", "test", "codeguard", "coverage", "sonar", "stryker" })]
    public void LoadFromFile_ParsesEachExampleProfile(string fileName, string expectedName, string[] expectedSteps)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ExampleProfiles", fileName);

        var profile = ValidationProfileLoader.LoadFromFile(path);

        Assert.Equal(expectedName, profile.Profile);
        Assert.Equal(expectedSteps, profile.Validation);
    }

    [Fact]
    public void LoadFromFile_ParsesSonarAndMutationThresholds_FromTheMediumExampleProfile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ExampleProfiles", "dotnet-medium.yml");

        var profile = ValidationProfileLoader.LoadFromFile(path);

        Assert.Equal("your-org_your-project", profile.Quality.Sonar.ProjectKey);
        Assert.Equal(0, profile.Quality.Sonar.NewBlocker);
        Assert.Equal(0, profile.Quality.Sonar.NewCritical);
        Assert.Equal(80, profile.Quality.Coverage.Minimum);
    }

    [Fact]
    public void LoadFromFile_ParsesMutationThreshold_FromTheThoroughExampleProfile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ExampleProfiles", "dotnet-thorough.yml");

        var profile = ValidationProfileLoader.LoadFromFile(path);

        Assert.Equal(70, profile.Quality.Mutation.Minimum);
    }

    private static string WriteTempYaml(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coderail-profile-{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, content);
        return path;
    }
}
