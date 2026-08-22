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

    /// <summary>Parses the same report's per-file line coverage, from the
    /// <c>packages/package/classes/class</c> elements Cobertura nests under the root-level
    /// aggregate attributes <see cref="Parse"/> reads. Used for changed-code coverage (docs §12) -
    /// the root attributes alone can't tell you which lines belong to which source file. A file
    /// covered by more than one <c>&lt;class&gt;</c> element (e.g. partial classes) has its lines
    /// aggregated under a single entry.</summary>
    public static IReadOnlyList<CoberturaFileCoverage> ParsePerFile(string path)
    {
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException($"'{path}' has no root element.");

        var byFile = new Dictionary<string, (double Covered, double Valid)>(StringComparer.OrdinalIgnoreCase);
        foreach (var classElement in root.Descendants("class"))
        {
            var fileName = (string?)classElement.Attribute("filename");
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            var lines = classElement.Element("lines")?.Elements("line").ToList() ?? [];
            var valid = lines.Count;
            var covered = lines.Count(line => ReadDouble(line, "hits") > 0);

            var existing = byFile.TryGetValue(fileName, out var accumulated) ? accumulated : (0, 0);
            byFile[fileName] = (existing.Item1 + covered, existing.Item2 + valid);
        }

        return byFile.Select(kvp => new CoberturaFileCoverage(kvp.Key, kvp.Value.Covered, kvp.Value.Valid)).ToList();
    }

    private static double ReadDouble(XElement element, string attributeName) =>
        double.TryParse((string?)element.Attribute(attributeName), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

public sealed record CoberturaSummary(double LinesCovered, double LinesValid, double BranchesCovered, double BranchesValid);

/// <summary>Line coverage for a single source file, as reported by one Cobertura
/// <c>&lt;class filename="..."&gt;</c> element (or the aggregate of several, for partial
/// classes).</summary>
public sealed record CoberturaFileCoverage(string FileName, double LinesCovered, double LinesValid);
