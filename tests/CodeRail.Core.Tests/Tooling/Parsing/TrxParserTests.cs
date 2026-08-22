using CodeRail.Tooling.Parsing;

namespace CodeRail.Tooling.Tests.Parsing;

public class TrxParserTests
{
    private const string SampleTrx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testName="Namespace.PassingTests.AlwaysPasses" outcome="Passed" />
            <UnitTestResult testName="Namespace.FailingTests.AlwaysFails" outcome="Failed">
              <Output>
                <ErrorInfo>
                  <Message>Assert.True() Failure</Message>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            <UnitTestResult testName="Namespace.SkippedTests.NotRun" outcome="NotExecuted" />
          </Results>
          <ResultSummary outcome="Failed">
            <Counters total="3" executed="2" passed="1" failed="1" />
          </ResultSummary>
        </TestRun>
        """;

    [Fact]
    public void Parse_ReadsCountersFromResultSummary()
    {
        var path = WriteTempTrx(SampleTrx);

        var summary = TrxParser.Parse(path);

        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Passed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public void Parse_ExtractsOneFailurePerFailedResult_WithErrorMessage()
    {
        var path = WriteTempTrx(SampleTrx);

        var summary = TrxParser.Parse(path);

        var failure = Assert.Single(summary.Failures);
        Assert.Equal("Namespace.FailingTests.AlwaysFails", failure.TestName);
        Assert.Equal("Assert.True() Failure", failure.ErrorMessage);
    }

    [Fact]
    public void Parse_TreatsMissingErrorInfo_AsNullMessage()
    {
        const string trx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="NoMessage.Test" outcome="Failed" />
              </Results>
              <ResultSummary outcome="Failed">
                <Counters total="1" executed="1" passed="0" failed="1" />
              </ResultSummary>
            </TestRun>
            """;
        var path = WriteTempTrx(trx);

        var summary = TrxParser.Parse(path);

        Assert.Null(Assert.Single(summary.Failures).ErrorMessage);
    }

    private static string WriteTempTrx(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coderail-trx-{Guid.NewGuid():N}.trx");
        File.WriteAllText(path, content);
        return path;
    }
}
