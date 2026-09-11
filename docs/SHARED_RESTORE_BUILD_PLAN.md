# Sharing restore/build across executors — what shipped, and what was rejected

> Implemented. This replaces the original speculative plan of the same name; the three ideas it
> proposed that were *not* built are recorded below with the reason, so they aren't re-proposed
> from the old text.

## The problem

In a `[build, test, coverage]` profile, `DotnetBuildExecutor`, `DotnetTestExecutor` and
`CoverageExecutor` each ran a plain `dotnet build`/`dotnet test`, so the same solution was restored
three times and compiled three times before any test executed. That waste landed on every AI
repair-loop iteration — the one thing the exit-code contract (docs §16) is supposed to make cheap.

## What shipped

A single flag on `ToolContext`:

```csharp
public sealed record ToolContext(
    string RepoRoot, IReadOnlyList<string> SolutionPaths, ChangeSet? Changes = null,
    SonarConfig? Sonar = null, bool SkipBuild = false);
```

`ValidationEngine.RunAsync` keeps a `sequentialContext` local and, after a `build` step returns
`Passed`, sets `SkipBuild = true` on it — so every step declared *after* `build` is told a compile
of exactly these `SolutionPaths` is already on disk. `DotnetTestExecutor` and `CoverageExecutor`
then append `--no-build` to the `dotnet test` command line they already build per target.

Confirmed on the installed SDK (`dotnet test --help`): **`--no-build` implies `--no-restore`**, so
only one flag is ever needed. This matches the design doc's own illustrative commands
(`HIGH_LEVEL_PLAN.md:331,350`).

Also: `SonarExecutor`'s internal `test --collect:"XPlat Code Coverage"` call takes `--no-build`
unconditionally — it always runs immediately after Sonar's own `dotnet build`, inside the same
`ExecuteAsync`. Sonar's `build` call must **never** take it: the scanner only collects analysis
data from a compile that happens between `begin` and `end`.

**Backward compatibility.** `SkipBuild` defaults to `false`, so a profile with no `build` step, a
profile that declares `test` before `build`, and every custom profile a user already has keep
self-restoring and self-building exactly as before. Verified from a cold `bin`/`obj` with a
`[test, coverage]`-only profile. Executors that can't use the flag (`codeguard`, `stryker`, Sonar's
scanner build) simply ignore it, the same way executors already ignore `Changes`/`Sonar`.

**Measured** on this repo (7 projects, warm build, test execution filtered out to isolate the
overhead): `dotnet test` 3.6s → `dotnet test --no-build` 1.6s, i.e. ~2s saved per invocation and
~4s per `[build, test, coverage]` run. It scales with the size of the target repo.

## Rejected: a separate `restore` step

The original plan added a `RestoreExecutor`, `ValidationSteps.Restore`, `ToolIds.Restore`, a
`PolicyEvaluator` arm, a `ToolDisplayNames` entry, `restore` prepended to four profile YAMLs, and
matching test updates — so that `build` could run `--no-restore`.

**It buys no speedup at all.** A `dotnet restore` step followed by `dotnet build --no-restore` does
exactly the same work as `dotnet build`'s implicit restore. 100% of the saving comes from
`--no-build` on `test`/`coverage`. The only real benefit left is evidence granularity (a distinct
restore-failure `ToolResult` instead of restore errors surfacing inside a `build-failure` finding),
which doesn't justify that much surface area. There is deliberately **no** `SkipRestore` companion
flag, because nothing would ever set it.

## Rejected: running `test` and `coverage` concurrently

The original plan ran the two concurrently once `build` passed, reasoning that with both on
`--no-build` neither touches `obj`/`bin`.

**That premise is false for coverage.** `coverlet.core.dll` exports `BackupOriginalModule`,
`RestoreOriginalModule` and `_backupList` (verified in
`~/.nuget/packages/coverlet.collector/10.0.1/build/net10.0/coverlet.core.dll`): the collector
instruments by rewriting the assemblies in `bin` **in place** and restoring them when the run ends.
Overlapping it with a plain `dotnet test` races a testhost that has those exact files loaded.

The real win here isn't concurrency anyway — it's that `test` and `coverage` execute the whole
suite twice. Merging them into a single `dotnet test` invocation emitting both TRX and Cobertura
removes an entire suite execution, which dwarfs everything above. That is the open follow-up.

## Rejected: `--no-restore` on Sonar's scanner build

The original plan gated it on `SkipRestore && SolutionPaths.Count == 0`, because Sonar ignores
`SolutionPaths` and auto-discovers at `RepoRoot`.

**That condition is dead code.** `ValidateCommand` always calls
`SolutionFileLocator.Resolve(repoRoot, [], …)`, so `SolutionPaths` is empty only when the target
repo contains no `.sln`/`.slnx` at all.

## Not applicable: Stryker

`dotnet stryker --help` exposes no restore- or build-skip flag of any kind (only
`--skip-version-check`) — Stryker.NET drives its own build of the mutated sources as part of
mutation testing. `StrykerExecutor` ignores `SkipBuild`; the reason is recorded in its remarks.
