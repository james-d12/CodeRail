using System.Xml.Linq;

namespace CodeRail.Tooling.Parsing;

/// <summary>Parses a VSTest .trx file (as produced by <c>dotnet test --logger trx</c>) into a
/// small summary. See <c>docs/HIGH_LEVEL_PLAN.md</c> §9.2.</summary>
public static class TrxParser
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static TrxTestRunSummary Parse(string trxPath)
    {
        var document = XDocument.Load(trxPath);
        var root = document.Root ?? throw new InvalidOperationException($"'{trxPath}' has no root element.");

        var counters = root.Element(Ns + "ResultSummary")?.Element(Ns + "Counters");
        var total = ReadInt(counters, "total");
        var passed = ReadInt(counters, "passed");
        var failed = ReadInt(counters, "failed");
        var skipped = Math.Max(0, total - passed - failed);

        var failures = root
            .Element(Ns + "Results")
            ?.Elements(Ns + "UnitTestResult")
            .Where(r => (string?)r.Attribute("outcome") == "Failed")
            .Select(r => new TrxTestFailure(
                (string?)r.Attribute("testName") ?? "(unknown test)",
                r.Element(Ns + "Output")?.Element(Ns + "ErrorInfo")?.Element(Ns + "Message")?.Value))
            .ToList() ?? [];

        return new TrxTestRunSummary(total, passed, failed, skipped, failures);
    }

    private static int ReadInt(XElement? counters, string attributeName) =>
        counters is not null && int.TryParse((string?)counters.Attribute(attributeName), out var value) ? value : 0;
}

public sealed record TrxTestRunSummary(int Total, int Passed, int Failed, int Skipped, IReadOnlyList<TrxTestFailure> Failures);

public sealed record TrxTestFailure(string TestName, string? ErrorMessage);
