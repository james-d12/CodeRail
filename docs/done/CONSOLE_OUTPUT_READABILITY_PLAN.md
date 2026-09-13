# Console Output Readability + JSON Severity Summary

> Implemented as planned, with one deliberate simplification: `ConsoleGateResultWriter` cannot see
> the real console, so color/TTY detection lives in `ValidateCommand` (format is `console` AND
> writing to stdout AND `!Console.IsOutputRedirected` AND no `NO_COLOR`), passed in as a `useColor`
> constructor flag - exactly as planned. Verified end-to-end with a real pty (`python3 -m pty`,
> `script` isn't installed in this environment): ANSI codes appear under a real terminal and are
> absent both when piped and under `NO_COLOR=1`; `--verbosity debug` restores the per-step chatter
> that's silent by default at `warning`; `--format json` gained the `summary` block; `--format sarif`
> is unchanged (`$schema`/`version`/`runs` only, no `summary` key). All 174 tests pass across the 4
> projects; `dotnet format --verify-no-changes` is clean.

## Context

`coderail validate`'s default console run interleaves two things that fight each other:
`ValidationEngine`'s per-step `ILogger` chatter (`Running step 'x'` / `Step 'x' finished: ...`,
emitted at `Information`, which is the CLI's current `--verbosity` default) on stderr, and the
final `QUALITY GATE` report on stdout. In a real terminal both streams land interleaved and the
result reads like a log dump, not a report. Separately, the report itself — `ConsoleGateResultWriter`
— renders `GateResult.BlockingFindings` as one flat, undeduplicated bullet list: the example output
that prompted this plan had the same `"Expected at least one match for a 'call_site' selector..."`
message repeated 5 times verbatim with no per-tool grouping, no counts, and no visual distinction
between severities. JSON and SARIF (`JsonGateResultWriter`, `SarifGateResultWriter`) already exist
and already satisfy docs §20's machine-readable requirement; the only gap there is a severity-count
summary for parity with the new console summary.

Decisions confirmed with the user: hide step chatter by default (opt in via `--verbosity debug`),
use auto-detected ANSI colors/symbols, group+dedupe+cap the findings list by tool, and add a small
severity-count summary to JSON only (SARIF untouched).

## Changes

### 1. Default verbosity → `warning`

`src/CodeRail.Cli/Support/CommonOptions.cs` — `CreateVerbosityOption`: change
`DefaultValueFactory` from `"information"` to `"warning"`, update the `Description` text
accordingly. This silences `ValidationEngine`'s `LogInformation` step-progress lines by default
(they remain available via `--verbosity debug`/`information`); `LogWarning` calls (build
short-circuit, missing executor) still show by default. No code changes needed in
`ValidationEngine` itself — its logging is unchanged, only what's visible by default shifts.
`CliLoggerFactory.ParseVerbosity` already accepts `warning`; no change needed there.

### 2. `GateResult` gets a computed `Summary` (feeds both console and JSON)

`src/CodeRail.Core/Policy/GateResult.cs` — add a new record and a **computed, non-constructor**
property so no call site that does `new GateResult(status, tools, findings, timestamp)` breaks and
record equality (which only covers primary-constructor-backed fields) is unaffected:

```csharp
public sealed record GateResult(
    ValidationStatus Status,
    IReadOnlyList<ToolResult> Tools,
    IReadOnlyList<Finding> BlockingFindings,
    DateTimeOffset EvaluatedAtUtc)
{
    public FindingSummary Summary => new(
        BlockingFindings.Count,
        BlockingFindings.Count(f => f.Severity == Severity.Critical),
        BlockingFindings.Count(f => f.Severity == Severity.Error),
        BlockingFindings.Count(f => f.Severity == Severity.Warning),
        BlockingFindings.Count(f => f.Severity == Severity.Info));
}

public sealed record FindingSummary(int Total, int Critical, int Error, int Warning, int Info);
```

Because `JsonGateResultWriter` does a plain `JsonSerializer.Serialize(result, Options)` of
`GateResult`, this appears in `--format json` output automatically as a `"summary"` object —
no writer change needed. `SarifGateResultWriter` builds its own `SarifLog` shape and never touches
`GateResult` directly, so SARIF output is unaffected, matching the decision to leave it untouched.

### 3. `ConsoleGateResultWriter` — grouped, deduped, colored report

`src/CodeRail.Core/Reporting/Console/ConsoleGateResultWriter.cs` — rewrite the body. Key pieces:

- **Constructor takes `bool useColor = false`.** The writer only ever sees a `TextWriter` (which
  may be a real console, a file `StreamWriter`, or a `StringWriter` in tests), so it cannot detect
  a terminal itself — the caller decides and passes the flag in.
- **Tool summary lines** gain a symbol (`✓`/`✗`) and duration, colored when `useColor`:
  `  ✓ Build       PASS   (1.2s)`. Reuse the existing `ToolDisplayNames.For` map; read timing from
  `ToolResult.Duration`.
- **Findings grouped by tool.** `BlockingFindings` carries no tool attribution (a known gap — see
  `docs/MCP_FEASIBILITY.md` §4.1), so derive the grouping instead of modifying the `Evidence`
  model: build a `HashSet<Finding>` from `result.BlockingFindings`, then for each `result.Tools`
  entry (preserving pipeline order) filter `tool.Findings.Where(blockingSet.Contains)`. This reuses
  the exact `Finding` values `PolicyEvaluator` already put in both places — no new field, no touch
  to `CodeRail.Evidence`.
- **Dedupe within a tool group** by `(Message, File, Line, RuleId)`; collapse identical entries to
  one line with a `(×N)` suffix when N > 1.
- **Cap each tool group** at a fixed count (10) with an overflow line, e.g.
  `… and 3 more — see --format json for the full list`, rather than dumping everything.
- **Header line** uses `result.Summary`: `Blocking findings: 11 (2 critical, 9 error)`.
- Keep the trailing `Action required:` line only when `BlockingFindings.Count > 0` (unchanged
  behavior).
- Small internal `AnsiCodes`-style helper (private static strings/consts) for green/red/yellow/dim/
  reset — no new dependency, plain ANSI escape sequences gated entirely behind `useColor`.

### 4. Wire color auto-detection in the CLI

`src/CodeRail.Cli/Commands/ValidateCommand.cs`:

- Compute `useColor` where the writer is created: true only when format is `"console"` **and**
  writing to stdout (`outputOption` value is null) **and** `!Console.IsOutputRedirected` **and**
  `Environment.GetEnvironmentVariable("NO_COLOR") is null`.
- `CreateWriter(format, useColor)` passes `useColor` into `new ConsoleGateResultWriter(useColor)`
  for the `"console"` case; `json`/`sarif` branches unchanged.

### 5. Tests

- `tests/CodeRail.Core.Tests/Reporting/Console/ConsoleGateResultWriterTests.cs` — update the two
  existing tests' expected strings for the new format, and add: dedupe collapsing (3 identical
  findings → one line with `(×3)`), cap+overflow (>10 findings in one tool → overflow line present,
  only 10 individual lines), color on/off (construct with `useColor: true` vs default `false` and
  assert ANSI escape presence/absence — this works directly against a `StringWriter`, independent
  of any real console).
- `tests/CodeRail.Core.Tests/Policy/GateResultTests.cs` (new) — verify `Summary` counts per
  severity from a constructed `BlockingFindings` list.
- `tests/CodeRail.Core.Tests/Reporting/Json/JsonGateResultWriterTests.cs` — add an assertion that
  serialized output contains a `"summary"` object with the expected counts.
- No changes expected to `tests/CodeRail.Cli.Tests/Commands/ValidateCommandTests.cs`,
  `tests/CodeRail.IntegrationTests/ValidateEndToEndTests.cs`, or `CliLoggerFactoryTests.cs` —
  confirmed none of them assert the default verbosity value or specific console report body text;
  they already pass `--verbosity error` explicitly where relevant.

## Verification

- `dotnet build` (0 warnings/errors), `dotnet test` (all 4 projects), `dotnet format
  --verify-no-changes`.
- Manual check: `dotnet run --project src/CodeRail.Cli -- validate --path .` in a real terminal —
  confirm no step-chatter lines by default, colored/symboled summary block, grouped+deduped+capped
  findings; re-run with `--verbosity debug` to confirm chatter reappears; `| cat` or `--output
  out.txt` to confirm color is suppressed when not a TTY; `NO_COLOR=1 dotnet run ...` to confirm
  explicit opt-out works.
- `dotnet run --project src/CodeRail.Cli -- validate --path . --format json` — confirm a
  `"summary"` object appears with correct counts; `--format sarif` — confirm output is byte-for-byte
  unchanged in shape (still no summary section).
