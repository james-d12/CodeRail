using CodeRail.Evidence;
using CodeRail.Tooling.Parsing;

namespace CodeRail.Tooling.Tests.Parsing;

public class CodeGuardJsonParserTests
{
    private const string SampleJson = """
        {
          "status": "failed",
          "rulesEvaluated": 42,
          "rulesPassed": 40,
          "rulesFailed": 2,
          "rulesErrored": 0,
          "violations": [
            {
              "ruleId": "ARCH-001",
              "severity": "critical",
              "message": "Domain layer references Infrastructure.",
              "file": "OrderService.cs",
              "line": 42
            },
            {
              "ruleId": "STYLE-001",
              "severity": "info",
              "message": "Prefer expression-bodied members."
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsStatusAndCounters()
    {
        var summary = CodeGuardJsonParser.Parse(SampleJson);

        Assert.Equal(ValidationStatus.Failed, summary.Status);
        Assert.Equal(42, summary.RulesEvaluated);
        Assert.Equal(40, summary.RulesPassed);
        Assert.Equal(2, summary.RulesFailed);
        Assert.Equal(0, summary.RulesErrored);
    }

    [Fact]
    public void Parse_MapsEachViolationToAFinding_WithSeverityAndLocation()
    {
        var summary = CodeGuardJsonParser.Parse(SampleJson);

        Assert.Equal(2, summary.Findings.Count);
        var critical = summary.Findings[0];
        Assert.Equal(Severity.Critical, critical.Severity);
        Assert.Equal("ARCH-001", critical.RuleId);
        Assert.Equal("OrderService.cs", critical.File);
        Assert.Equal(42, critical.Line);

        var info = summary.Findings[1];
        Assert.Equal(Severity.Info, info.Severity);
        Assert.Null(info.File);
        Assert.Null(info.Line);
    }

    [Fact]
    public void Parse_TreatsMissingViolations_AsEmptyList()
    {
        const string json = """{ "status": "passed", "rulesEvaluated": 5, "rulesPassed": 5, "rulesFailed": 0, "rulesErrored": 0 }""";

        var summary = CodeGuardJsonParser.Parse(json);

        Assert.Equal(ValidationStatus.Passed, summary.Status);
        Assert.Empty(summary.Findings);
    }

    [Fact]
    public void Parse_TreatsUnrecognizedStatus_AsPartiallyEvaluated()
    {
        const string json = """{ "status": "somethingNew" }""";

        var summary = CodeGuardJsonParser.Parse(json);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, summary.Status);
    }
}
