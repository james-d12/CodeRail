using System.Globalization;
using System.Xml.Linq;

namespace CodeRail.Tooling.Parsing;

/// <summary>Parses a Cobertura coverage report (as produced by
/// <c>dotnet test --collect:"XPlat Code Coverage"</c> when the target project references
/// <c>coverlet.collector</c>) into raw line/branch counts. See
/// <c>docs/HIGH_LEVEL_PLAN.md</c> §9.3.</summary>
public static class CoberturaParser
{
    public static CoberturaSummary Parse(string path)
    {
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException($"'{path}' has no root element.");

        return new CoberturaSummary(
            ReadDouble(root, "lines-covered"),
            ReadDouble(root, "lines-valid"),
            ReadDouble(root, "branches-covered"),
            ReadDouble(root, "branches-valid"));
    }

    private static double ReadDouble(XElement element, string attributeName) =>
        double.TryParse((string?)element.Attribute(attributeName), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

public sealed record CoberturaSummary(double LinesCovered, double LinesValid, double BranchesCovered, double BranchesValid);
