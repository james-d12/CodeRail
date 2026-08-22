using CodeRail.Evidence;
using CodeRail.Tooling.Parsing;

namespace CodeRail.Tooling.Tests.Parsing;

public class SonarIssuesParserTests
{
    [Fact]
    public void Parse_StripsTheProjectKeyPrefix_FromEachIssuesComponent()
    {
        const string json = """
            {
              "issues": [
                { "rule": "csharpsquid:S1481", "severity": "MINOR", "component": "my-project:src/Foo.cs", "message": "Unused variable", "line": 12 }
              ]
            }
            """;

        var findings = SonarIssuesParser.Parse(json, "my-project");

        var finding = Assert.Single(findings);
        Assert.Equal("src/Foo.cs", finding.File);
        Assert.Equal(12, finding.Line);
        Assert.Equal("csharpsquid:S1481", finding.RuleId);
        Assert.Equal("Unused variable", finding.Message);
    }

    [Fact]
    public void Parse_LeavesComponent_UnchangedWhenItDoesNotHaveTheExpectedPrefix()
    {
        const string json = """{ "issues": [ { "component": "other-project:src/Foo.cs", "severity": "MAJOR" } ] }""";

        var findings = SonarIssuesParser.Parse(json, "my-project");

        Assert.Equal("other-project:src/Foo.cs", Assert.Single(findings).File);
    }

    [Theory]
    [InlineData("BLOCKER", Severity.Critical)]
    [InlineData("CRITICAL", Severity.Error)]
    [InlineData("MAJOR", Severity.Warning)]
    [InlineData("MINOR", Severity.Warning)]
    [InlineData("INFO", Severity.Info)]
    [InlineData(null, Severity.Info)]
    public void Parse_MapsSonarSeverity_OntoCodeRailSeverity(string? sonarSeverity, Severity expected)
    {
        var severityJson = sonarSeverity is null ? "null" : $"\"{sonarSeverity}\"";
        var json = $$"""{ "issues": [ { "severity": {{severityJson}}, "component": "p:F.cs" } ] }""";

        var findings = SonarIssuesParser.Parse(json, "p");

        Assert.Equal(expected, Assert.Single(findings).Severity);
    }

    [Fact]
    public void Parse_DefaultsMessage_WhenMissing()
    {
        const string json = """{ "issues": [ { "severity": "MAJOR", "component": "p:F.cs" } ] }""";

        var findings = SonarIssuesParser.Parse(json, "p");

        Assert.Equal("(no message)", Assert.Single(findings).Message);
    }

    [Fact]
    public void Parse_ReturnsEmpty_WhenThereAreNoIssues()
    {
        const string json = """{ "issues": [] }""";

        var findings = SonarIssuesParser.Parse(json, "p");

        Assert.Empty(findings);
    }
}
