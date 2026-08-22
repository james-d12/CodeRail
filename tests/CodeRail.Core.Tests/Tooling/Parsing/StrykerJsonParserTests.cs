using CodeRail.Tooling.Parsing;

namespace CodeRail.Tooling.Tests.Parsing;

public class StrykerJsonParserTests
{
    [Fact]
    public void Parse_ComputesMutationScore_AsDetectedOverValid()
    {
        // detected = killed(2) + timeout(1) = 3; valid = detected(3) + survived(1) + noCoverage(1) = 5
        // => 3 / 5 * 100 = 60%.
        const string json = """
            {
              "files": {
                "Calculator.cs": {
                  "mutants": [
                    { "id": "1", "mutatorName": "Arithmetic", "status": "Killed", "location": { "start": { "line": 1 }, "end": { "line": 1 } } },
                    { "id": "2", "mutatorName": "Arithmetic", "status": "Killed", "location": { "start": { "line": 2 }, "end": { "line": 2 } } },
                    { "id": "3", "mutatorName": "Arithmetic", "status": "Timeout", "location": { "start": { "line": 3 }, "end": { "line": 3 } } },
                    { "id": "4", "mutatorName": "Boolean", "status": "Survived", "location": { "start": { "line": 4 }, "end": { "line": 4 } } },
                    { "id": "5", "mutatorName": "Boolean", "status": "NoCoverage", "location": { "start": { "line": 5 }, "end": { "line": 5 } } }
                  ]
                }
              }
            }
            """;

        var summary = StrykerJsonParser.Parse(json);

        Assert.Equal(60.0, summary.MutationScore);
        Assert.Equal(2, summary.Killed);
        Assert.Equal(1, summary.Survived);
        Assert.Equal(1, summary.NoCoverage);
        Assert.Equal(1, summary.Timeout);
    }

    [Fact]
    public void Parse_ExcludesCompileErrorRuntimeErrorIgnoredAndPending_FromTheMutationScore()
    {
        const string json = """
            {
              "files": {
                "Calculator.cs": {
                  "mutants": [
                    { "id": "1", "mutatorName": "Arithmetic", "status": "Killed", "location": { "start": { "line": 1 }, "end": { "line": 1 } } },
                    { "id": "2", "mutatorName": "Arithmetic", "status": "CompileError", "location": { "start": { "line": 2 }, "end": { "line": 2 } } },
                    { "id": "3", "mutatorName": "Arithmetic", "status": "RuntimeError", "location": { "start": { "line": 3 }, "end": { "line": 3 } } },
                    { "id": "4", "mutatorName": "Arithmetic", "status": "Ignored", "location": { "start": { "line": 4 }, "end": { "line": 4 } } },
                    { "id": "5", "mutatorName": "Arithmetic", "status": "Pending", "location": { "start": { "line": 5 }, "end": { "line": 5 } } }
                  ]
                }
              }
            }
            """;

        var summary = StrykerJsonParser.Parse(json);

        // Only the one Killed mutant is scoreable - the rest don't count toward valid at all.
        Assert.Equal(100.0, summary.MutationScore);
        Assert.Empty(summary.Findings);
    }

    [Fact]
    public void Parse_ProducesAFinding_PerSurvivedOrUncoveredMutant_WithFileAndLine()
    {
        const string json = """
            {
              "files": {
                "Calculator.cs": {
                  "mutants": [
                    { "id": "1", "mutatorName": "ArithmeticOperator", "status": "Survived", "location": { "start": { "line": 7 }, "end": { "line": 7 } } },
                    { "id": "2", "mutatorName": "Boolean", "status": "NoCoverage", "location": { "start": { "line": 12 }, "end": { "line": 12 } } }
                  ]
                }
              }
            }
            """;

        var summary = StrykerJsonParser.Parse(json);

        Assert.Equal(2, summary.Findings.Count);
        var survived = Assert.Single(summary.Findings, f => f.Type == "mutation-survived");
        Assert.Equal("Calculator.cs", survived.File);
        Assert.Equal(7, survived.Line);
        Assert.Contains("ArithmeticOperator", survived.Message);

        var noCoverage = Assert.Single(summary.Findings, f => f.Type == "mutation-no-coverage");
        Assert.Equal("Calculator.cs", noCoverage.File);
        Assert.Equal(12, noCoverage.Line);
    }

    [Fact]
    public void Parse_ReturnsNullMutationScore_WhenThereAreNoScoreableMutants()
    {
        const string json = """
            {
              "files": {
                "Calculator.cs": {
                  "mutants": [
                    { "id": "1", "mutatorName": "Arithmetic", "status": "CompileError", "location": { "start": { "line": 1 }, "end": { "line": 1 } } }
                  ]
                }
              }
            }
            """;

        var summary = StrykerJsonParser.Parse(json);

        Assert.Null(summary.MutationScore);
    }

    [Fact]
    public void Parse_ReturnsNullMutationScore_WhenReportHasNoFiles()
    {
        const string json = """{ "files": {} }""";

        var summary = StrykerJsonParser.Parse(json);

        Assert.Null(summary.MutationScore);
        Assert.Equal(0, summary.Killed);
        Assert.Empty(summary.Findings);
    }
}
