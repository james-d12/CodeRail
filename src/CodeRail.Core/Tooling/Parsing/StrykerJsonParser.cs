using System.Text.Json;
using CodeRail.Evidence;

namespace CodeRail.Tooling.Parsing;

/// <summary>
/// Parses Stryker.NET's <c>mutation-report.json</c> - the "mutation-testing-elements" report
/// format (<c>docs/HIGH_LEVEL_PLAN.md</c> §9.5) - into CodeRail's evidence model. Treated purely as
/// an external wire contract, same as <see cref="CodeGuardJsonParser"/> - no dependency on
/// Stryker's own assemblies.
/// </summary>
/// <remarks>
/// Mutation score follows Stryker's own definition: <c>detected / valid * 100</c>, where
/// <c>detected = killed + timeout</c> and <c>valid = detected + survived + noCoverage</c>.
/// <c>CompileError</c>, <c>RuntimeError</c>, <c>Ignored</c>, and <c>Pending</c> mutants are
/// excluded from the score entirely and don't produce findings - they aren't evidence of a test
/// gap the way a survived or uncovered mutant is.
/// </remarks>
public static class StrykerJsonParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static StrykerMutationSummary Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<RawReport>(json, Options)
            ?? throw new JsonException("Stryker JSON report deserialized to null.");

        int killed = 0, survived = 0, noCoverage = 0, timeout = 0;
        var findings = new List<Finding>();

        foreach (var (filePath, file) in raw.Files ?? new Dictionary<string, RawFile>())
        {
            foreach (var mutant in file.Mutants ?? [])
            {
                switch (mutant.Status?.ToLowerInvariant())
                {
                    case "killed":
                        killed++;
                        break;
                    case "timeout":
                        timeout++;
                        break;
                    case "survived":
                        survived++;
                        findings.Add(MutantFinding("mutation-survived", "Mutation survived - no test caught this change", filePath, mutant));
                        break;
                    case "nocoverage":
                        noCoverage++;
                        findings.Add(MutantFinding("mutation-no-coverage", "Mutation not covered by any test", filePath, mutant));
                        break;
                        // CompileError, RuntimeError, Ignored, Pending: excluded from the score and
                        // not reported as findings - see the type-level remarks.
                }
            }
        }

        var detected = killed + timeout;
        var valid = detected + survived + noCoverage;
        double? mutationScore = valid > 0 ? Math.Round((double)detected / valid * 100, 2) : null;

        return new StrykerMutationSummary(mutationScore, killed, survived, noCoverage, timeout, findings);
    }

    private static Finding MutantFinding(string type, string reason, string filePath, RawMutant mutant)
    {
        var mutatorName = mutant.MutatorName ?? "unknown mutator";
        var line = mutant.Location?.Start?.Line;
        return new Finding(type, Severity.Warning, $"{reason} ({mutatorName}).", filePath, line, null);
    }

    private sealed record RawReport(Dictionary<string, RawFile>? Files);

    private sealed record RawFile(List<RawMutant>? Mutants);

    private sealed record RawMutant(string? MutatorName, string? Status, RawLocation? Location);

    private sealed record RawLocation(RawPosition? Start);

    private sealed record RawPosition(int? Line);
}

/// <summary>A whole-report mutation summary. <see cref="MutationScore"/> is null when there are no
/// scoreable mutants at all (e.g. every mutant in the report was a compile error).</summary>
public sealed record StrykerMutationSummary(
    double? MutationScore, int Killed, int Survived, int NoCoverage, int Timeout, IReadOnlyList<Finding> Findings);
