# Plan: Share restore/build across executors; parallelize test+coverage after build

> Implementation plan, not yet built. Follows on from the CodeGuard-parallelism change described
> in `CLAUDE.md`'s "Executors self-restore/self-build" section, which this plan directly addresses.

## Context

`DotnetBuildExecutor`, `DotnetTestExecutor`, `CoverageExecutor`, `StrykerExecutor`, and (partially)
`SonarExecutor` each independently self-restore (implicit `dotnet restore`) and, except Stryker,
self-build against the *same* solutions under `context.RepoRoot` - a known, documented inefficiency
(`CLAUDE.md`'s "Executors self-restore/self-build" section: "`DotnetTestExecutor` and
`CoverageExecutor` each redo their own restore+build+test rather than sharing one incremental
build ... worth revisiting once the engine can share intermediate output between steps"). In a
sequential pipeline (`restore` doesn't even exist as a step today) that means every one of
build/test/coverage repeats a full NuGet restore, and test/coverage each repeat a full compile
that `build` already just did, one after another.

This plan makes that sharing real: a new `restore` step runs once, `build` reuses its result, and
`test`/`coverage` reuse `build`'s already-compiled output - turning 3+ redundant
restore+build+test cycles into one restore, one build, and two lightweight `--no-build` test runs.
Confirmed (via `dotnet build -h` / `dotnet test -h`) that `dotnet test --no-build` **implies**
`--no-restore`, so only one flag is ever needed at a time. Once `test`/`coverage` no longer touch
`obj`/`bin` at all (both running `--no-build`), the MSBuild file-lock collision risk that kept them
sequential in the CodeGuard-parallelism plan (see `CLAUDE.md`'s same paragraph, updated by that
work) no longer applies to *that pair specifically* - so this plan also runs them concurrently
with each other once `build` has passed, mirroring the existing `codeguard`-runs-independently
mechanism in `ValidationEngine`.

**Stryker is confirmed excluded**: `dotnet stryker --help` has no restore/build-skip flag of any
kind (checked directly against the installed `dotnet-stryker 4.8.1`) - Stryker.NET manages its own
build/restore internally as part of mutation testing and there's no hook to shortcut it. No
`StrykerExecutor` code changes; just a doc-comment noting why.

**Sonar gets a smaller, self-contained win**: `SonarExecutor`'s own internal `dotnet build` →
`dotnet test --collect:"XPlat Code Coverage"` pair (docs §9.6) already runs back-to-back with
nothing in between that would invalidate the build, so its `test` call can safely add `--no-build`
unconditionally - independent of the cross-step sharing below. Its `build` call can additionally
take `--no-restore` when a shared restore already covered exactly what Sonar's own (`SolutionPaths`
-ignoring, repo-root-auto-discovering) build will target - see the `SonarExecutor` section below
for the precise, narrower condition needed there.

**Backward compatibility is the guiding constraint**: any profile that doesn't include `restore`
(all 3 `tests/CodeRail.IntegrationTests/Profiles/*.yml` files deliberately don't, and any existing
custom profile a user already has won't either) must keep working exactly as it does today -
executors self-restore/self-build unless told a prior step already did it for them in *this run*.

## Design

### New `restore` step

Add `RestoreExecutor` (new file, mirrors `DotnetBuildExecutor.cs` almost exactly: same
`context.SolutionPaths.Count > 0 ? SolutionPaths : [RepoRoot]` per-target loop, same
`isRepoRoot ? "restore" : $"restore \"{target}\""` argument shape, same `context.RepoRoot` working
directory, same exit-code-or-timeout pass/fail with a `"restore-failure"` `Finding`, same
`solutionsRestored`/`solutionsFailed`-style metrics). A shorter timeout than build's 10 minutes is
reasonable (e.g. 5 minutes) since restore alone is normally faster.

Add `ValidationSteps.Restore = "restore"` and `ToolIds.Restore = "dotnet-restore"`
(`src/CodeRail.Evidence/`). In `PolicyEvaluator.EvaluateTool`
(`src/CodeRail.Core/Policy/PolicyEvaluator.cs:39-61`), add a dedicated arm mirroring the existing
build one exactly:
```csharp
ToolIds.Restore when result.Status != ValidationStatus.Passed => result.Findings,
```
and add `ToolIds.Restore` to `IsKnownToolId` (line 63-64) - consistent with how `DotnetBuild`
already gets a dedicated unconditional-block arm rather than relying on the generic
"unrecognized tool" fallback.

Register `RestoreExecutor` in `ValidateCommand.BuildEngine`'s executor array
(`src/CodeRail.Cli/Commands/ValidateCommand.cs:132-138`), and add `restore` as the **first** step
in the embedded default profile (`src/CodeRail.Core/Configuration/Profiles/dotnet-default.yml`)
and all three example profiles (`examples/profiles/dotnet-{fast,medium,thorough}.yml`).

### `ToolContext` gets two new flags

`ToolContext` (`src/CodeRail.Core/Tooling/IToolExecutor.cs:31-32`) gains two new trailing optional
fields, defaulting to `false` (backward compatible with every existing positional/named
constructor call - confirmed via grep that no caller passes more than 4 positional args or uses
`with` in a way these would collide with):
```csharp
SkipRestore = false, SkipBuild = false
```
Every existing self-restoring/self-building executor ignores these unless it recognizes them
(same pattern as executors already ignoring `Changes`/`Sonar` when irrelevant to them).

### `ValidationEngine` threads an evolving context and adds one concurrent pair

Building on the current `StepsRunIndependently = { CodeGuard }` mechanism
(`src/CodeRail.Core/Engine/ValidationEngine.cs`):

1. Generalize the short-circuit gate from `step == ValidationSteps.Build` to a small set:
   `StepsThatGateThePipeline = { ValidationSteps.Restore, ValidationSteps.Build }` - if either
   fails, break (same as today's build-only check; since `restore` is always declared before
   `build`, a failed restore never even lets `build` start).
2. Thread a local `context` variable through the sequential loop, updated via `with` after each
   gating step passes: after `restore` passes, `context = context with { SkipRestore = true }`;
   after `build` passes, `context = context with { SkipRestore = true, SkipBuild = true }`. Every
   subsequent executor call (sequential loop and the codeguard-style independent runs already
   launched before this point) uses whichever `context` was current when it started - codeguard
   already ignores these two fields entirely, same as it ignores `Changes`/`Sonar`.
3. Add a second concurrency mechanism, alongside `StepsRunIndependently`, for exactly the
   `{ Test, Coverage }` pair: when the sequential loop reaches whichever of `test`/`coverage` is
   declared **first** in the profile, if `context.SkipBuild` is already `true` at that point *and*
   the other one of the pair is also present in the profile and hasn't run yet, launch both
   `ExecuteAsync` calls together (same "start now, await later" shape as the independent-step
   mechanism) instead of one after the other; when the loop naturally reaches the second one's
   position, just await the already-started task instead of invoking it again. If `build` isn't in
   the profile at all (so `SkipBuild` is never set), or only one of the pair is present, or the
   other one already ran earlier (an unusual profile order, e.g. `coverage` declared before
   `build`) - fall back to today's plain sequential behavior for that step. This keeps every
   existing/custom profile shape exactly as safe as it is today; the concurrency only activates for
   the specific case it's proven safe for (build's output already compiled and about to be reused
   by both, so neither touches `obj`/`bin`).
4. Reassembly into profile-declared order (already implemented for `codeguard`) covers this pair
   automatically - no new reordering logic needed.

### Flag logic in each executor

Same conditional shape in `DotnetBuildExecutor`, `DotnetTestExecutor`, `CoverageExecutor` - add a
suffix to the arguments string already being built per-target:

- **`DotnetBuildExecutor.cs`**: `context.SkipRestore ? " --no-restore" : ""`.
- **`DotnetTestExecutor.cs`** and **`CoverageExecutor.cs`** (identical logic in both): `context.SkipBuild
  ? " --no-build" : context.SkipRestore ? " --no-restore" : ""` (never both flags - `--no-build`
  already implies `--no-restore`, confirmed via `dotnet test -h`).
- **`SonarExecutor.cs`**: its own internal `"build"` call
  (`src/CodeRail.Core/Tooling/Executors/SonarExecutor.cs` ~line 87) gets
  `context.SkipRestore && context.SolutionPaths.Count == 0 ? " --no-restore" : ""` - the
  `SolutionPaths.Count == 0` guard matters because Sonar ignores `SolutionPaths` and always
  auto-discovers at `RepoRoot`; only when the shared restore *also* targeted "auto-discover at
  RepoRoot" (i.e. no explicit solutions were resolved) is it guaranteed to have restored the same
  project set Sonar's build will target - with explicit multi-solution `SolutionPaths`, a
  restore-per-solution pass doesn't necessarily cover Sonar's own repo-root-auto-discovered build,
  so leave Sonar self-restoring in that case. Its own internal `"test --collect:..."` call always
  gets `" --no-build"` appended unconditionally (self-contained: it always runs immediately after
  Sonar's own just-completed build within the same `ExecuteAsync` call, regardless of the
  cross-step sharing above).
- **`StrykerExecutor.cs`**: no code change - add a one-line doc-comment noting `dotnet stryker
  --help` has no restore/build-skip flag (confirmed against the installed `dotnet-stryker 4.8.1`),
  so it's excluded from this optimization.

### Files to change

- **New**: `src/CodeRail.Core/Tooling/Executors/RestoreExecutor.cs`.
- **`src/CodeRail.Evidence/ValidationSteps.cs`**, **`ToolIds.cs`**: add `Restore` constants.
- **`src/CodeRail.Core/Tooling/IToolExecutor.cs`**: add `SkipRestore`/`SkipBuild` to `ToolContext`.
- **`src/CodeRail.Core/Engine/ValidationEngine.cs`**: the generalized gate set, evolving `context`,
  and the new test/coverage concurrent-pair mechanism described above.
- **`src/CodeRail.Core/Policy/PolicyEvaluator.cs`**: the new `ToolIds.Restore` arm + `IsKnownToolId`.
- **`src/CodeRail.Core/Tooling/Executors/{DotnetBuildExecutor,DotnetTestExecutor,CoverageExecutor,SonarExecutor}.cs`**:
  flag logic above. `StrykerExecutor.cs`: doc-comment only.
- **`src/CodeRail.Cli/Commands/ValidateCommand.cs`**: register `RestoreExecutor`.
- **`src/CodeRail.Core/Configuration/Profiles/dotnet-default.yml`** and
  **`examples/profiles/dotnet-{fast,medium,thorough}.yml`**: prepend `restore` to `validation:`.
- **`CLAUDE.md`**: rewrite the "Executors self-restore/self-build" paragraph - the inefficiency it
  describes is what this change fixes; note the `restore`/`SkipRestore`/`SkipBuild` mechanism,
  Stryker's confirmed exclusion, and that `test`/`coverage` now run concurrently when build passed.
  Update "Scope: which executors exist" to say seven executors, not six.

### Tests to update

- **`tests/CodeRail.Core.Tests/Configuration/ValidationProfileLoaderTests.cs`**: prepend
  `"restore"` to the four expected step-list arrays (lines 11, 69-71) matching the YAML changes.
- **`tests/CodeRail.Core.Tests/Engine/ValidationEngineTests.cs`** /
  **`FakeToolExecutor.cs`**: add a `LastContext` capture to `FakeToolExecutor` (same shape as
  `LastCancellationToken`) to assert `SkipRestore`/`SkipBuild` reach the right steps. Add: a
  restore-failure short-circuit test (mirrors the existing build-failure one); a test asserting
  `build`'s received context has `SkipRestore == true` after a passing `restore`; a test asserting
  `test`/`coverage` received contexts have `SkipBuild == true` after a passing `build`; a
  concurrency test for the `{Test, Coverage}` pair using the same `TaskCompletionSource`-blocking
  `FakeToolExecutor` pattern added in the CodeGuard-parallelism change (assert one completes while
  the other is still gated, when `build` passed); and a test confirming a profile without `build`
  (e.g. `[test, coverage]` alone) keeps running them sequentially (no premature pairing) - proving
  the fallback-safety condition the user asked about.
- **`tests/CodeRail.Core.Tests/Tooling/Executors/`**: new `RestoreExecutorTests.cs` mirroring
  `DotnetBuildExecutorTests.cs`'s real-`ProcessRunner`-against-fixtures pattern. Add
  `StubProcessRunner`-based cases to `DotnetBuildExecutorTests.cs`/`DotnetTestExecutorTests.cs`
  (currently real-process tests; add stub-based cases alongside, following
  `CoverageExecutorTests.cs`'s existing pattern) and extend `CoverageExecutorTests.cs` directly,
  asserting the exact argument string for `SkipRestore`/`SkipBuild` true and false (regression
  protection for the default-false/unchanged-today path). Update `SonarExecutorTests.cs` to assert
  `--no-build` always appears on the test call and `--no-restore` appears on the build call only
  under the `SkipRestore && SolutionPaths.Count == 0` condition.
- **`tests/CodeRail.Core.Tests/Policy/PolicyEvaluatorTests.cs`**: add a
  `Evaluate_BlocksGate_WhenRestoreFails` test mirroring the existing build-failure test (~line 33).
- **`tests/CodeRail.IntegrationTests/Profiles/no-codeguard.yml`** (or `strict-coverage.yml`): add
  `restore` as the first step, so the real end-to-end suite exercises the actual `--no-restore`/
  `--no-build` flags against real fixture projects, not just unit-level argument assertions.

## Verification

- `dotnet build` (0 errors/warnings), `dotnet test` (all four projects), `dotnet format
  --verify-no-changes`.
- Run `dotnet run --project src/CodeRail.Cli -- validate --path <repo> --format json` against a
  real fixture with the updated `dotnet-fast`/`dotnet-medium` example profiles (both now start with
  `restore`), confirming: `restore` runs first and its result appears first in output; a failing
  `restore` (e.g. temporarily point at an unresolvable package/feed) short-circuits everything
  except `codeguard`; `test`/`coverage` logs show `--no-build` once `build` has passed; a profile
  omitting `restore`/`build` still works via the self-restore/self-build fallback.
- Time a full `validate` run against a real fixture before/after informally (not asserted in
  tests) to confirm the restore/build sharing plus test/coverage concurrency actually reduces
  wall-clock time versus the current sequential self-restoring/self-building behavior.
