using CodeRail.Tooling.Parsing;

namespace CodeRail.Tooling.Tests.Parsing;

public class CoberturaParserTests
{
    [Fact]
    public void Parse_ReadsLineAndBranchCountsFromRootAttributes()
    {
        const string cobertura = """
            <?xml version="1.0" encoding="UTF-8"?>
            <coverage line-rate="0.85" branch-rate="0.7" lines-covered="120" lines-valid="141" branches-covered="34" branches-valid="48" version="1.9" timestamp="0">
            </coverage>
            """;
        var path = WriteTempCobertura(cobertura);

        var summary = CoberturaParser.Parse(path);

        Assert.Equal(120, summary.LinesCovered);
        Assert.Equal(141, summary.LinesValid);
        Assert.Equal(34, summary.BranchesCovered);
        Assert.Equal(48, summary.BranchesValid);
    }

    [Fact]
    public void Parse_DefaultsMissingAttributes_ToZero()
    {
        const string cobertura = """<coverage version="1.9" timestamp="0"></coverage>""";
        var path = WriteTempCobertura(cobertura);

        var summary = CoberturaParser.Parse(path);

        Assert.Equal(0, summary.LinesCovered);
        Assert.Equal(0, summary.LinesValid);
    }

    private static string WriteTempCobertura(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coderail-cobertura-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, content);
        return path;
    }
}
