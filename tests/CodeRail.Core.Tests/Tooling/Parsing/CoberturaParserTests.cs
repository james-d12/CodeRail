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

    [Fact]
    public void ParsePerFile_ReadsLineCoverage_PerClassFilename()
    {
        const string cobertura = """
            <?xml version="1.0" encoding="UTF-8"?>
            <coverage version="1.9" timestamp="0">
              <packages>
                <package name="Lib">
                  <classes>
                    <class name="Lib.Calculator" filename="Lib/Calculator.cs">
                      <lines>
                        <line number="5" hits="3" />
                        <line number="6" hits="0" />
                        <line number="7" hits="1" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var path = WriteTempCobertura(cobertura);

        var files = CoberturaParser.ParsePerFile(path);

        var file = Assert.Single(files);
        Assert.Equal("Lib/Calculator.cs", file.FileName);
        Assert.Equal(2, file.LinesCovered);
        Assert.Equal(3, file.LinesValid);
    }

    [Fact]
    public void ParsePerFile_AggregatesMultipleClassesForTheSameFile()
    {
        // Partial classes: two <class> elements can point at the same source file.
        const string cobertura = """
            <?xml version="1.0" encoding="UTF-8"?>
            <coverage version="1.9" timestamp="0">
              <packages>
                <package name="Lib">
                  <classes>
                    <class name="Lib.Calculator" filename="Lib/Calculator.cs">
                      <lines>
                        <line number="5" hits="1" />
                      </lines>
                    </class>
                    <class name="Lib.Calculator.Nested" filename="Lib/Calculator.cs">
                      <lines>
                        <line number="20" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var path = WriteTempCobertura(cobertura);

        var files = CoberturaParser.ParsePerFile(path);

        var file = Assert.Single(files);
        Assert.Equal("Lib/Calculator.cs", file.FileName);
        Assert.Equal(1, file.LinesCovered);
        Assert.Equal(2, file.LinesValid);
    }

    [Fact]
    public void ParsePerFile_ReturnsEmpty_WhenReportHasNoClasses()
    {
        const string cobertura = """<coverage version="1.9" timestamp="0"></coverage>""";
        var path = WriteTempCobertura(cobertura);

        var files = CoberturaParser.ParsePerFile(path);

        Assert.Empty(files);
    }

    private static string WriteTempCobertura(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coderail-cobertura-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, content);
        return path;
    }
}
