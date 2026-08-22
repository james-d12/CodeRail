# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

CodeRail is a deterministic quality-validation and orchestration layer for AI-assisted software
development. It runs existing engineering tools (`dotnet build`/`test`, CodeGuard, coverage
collection), normalises their output into a common evidence model (`ToolResult`/`Finding`),
applies configurable quality-gate policy, and reports a single PASS/FAIL verdict — so an AI coding
agent gets a deterministic signal instead of self-declaring "done". Full design rationale lives in
`docs/HIGH_LEVEL_PLAN.md` — read it before proposing architectural changes. `../CodeGuard` (same
author) is the sibling project this repo's tooling/structure conventions are deliberately modelled
on, and is also CodeRail's first-class rules-engine integration (`CodeGuardExecutor`).

## Commands

```bash
dotnet build                    # 0 errors, 0 warnings expected
dotnet test                     # all tests across 4 test projects should pass
dotnet format --verify-no-changes

# CLI (AssemblyName=coderail):
dotnet run --project src/CodeRail.Cli -- validate --path <repo>
dotnet run --project src/CodeRail.Cli -- validate --path <repo> --profile <file.yml> --format json
```

CI (`.github/workflows/ci.yml`) runs `dotnet restore && dotnet format --verify-no-changes &&
dotnet build --no-restore && dotnet test --no-build` on `ubuntu-latest` for push/PR to `main`.
`global.json` pins the SDK to `10.0.100` (`rollForward: latestFeature`).

## Architecture

### Projects

```
CodeRail.Evidence   (no deps - pure record model: ToolResult, Finding, Severity, ValidationStatus,
                     Artifact, ToolIds, ValidationSteps - the common evidence model, docs §10)
  ^
  |-- CodeRail.Core  (everything else, one assembly, namespaced by concern - see below)
  |
CodeRail.Cli         (`coderail` dotnet tool; depends on Evidence + Core)
```

Originally scaffolded as 8 separate projects mirroring each design-doc section one-for-one
(`CodeRail.Execution`, `.Tooling`, `.Policy`, `.Configuration`, `.Engine`, `.Reporting`); collapsed
into a single `CodeRail.Core` early on since an MVP this size didn't warrant that many assemblies.
The internal namespace boundaries below are kept exactly as they were as separate projects, so the
seams still exist if a future split is ever justified - moving a namespace back out to its own
project should be a "move files + fix csproj" operation, not a redesign.

### Namespaces inside `CodeRail.Core`

```
CodeRail.Execution            IProcessRunner / ProcessRunner / ProcessResult (docs §8) - the one
                               execution abstraction every executor goes through
CodeRail.Tooling               IToolExecutor / ToolContext / SolutionFileLocator (docs §7)
CodeRail.Tooling.Executors     DotnetBuildExecutor, DotnetTestExecutor, CodeGuardExecutor,
                                CoverageExecutor
CodeRail.Tooling.Parsing       TrxParser, CoberturaParser, CodeGuardJsonParser
CodeRail.Policy                QualityProfile, GateResult, PolicyEvaluator (docs §11) - pure,
                                no I/O; turns ToolResults + thresholds into a GateResult
CodeRail.Configuration          ValidationProfile, ValidationProfileLoader (docs §13) - YAML via
                                YamlDotNet; embedded default profile at
                                Configuration/Profiles/dotnet-default.yml
CodeRail.Engine                 ValidationEngine (docs §6.1) - runs a profile's steps in order,
                                short-circuits the rest of the pipeline if `build` fails
CodeRail.Reporting              IGateResultWriter + Console/Json writers (docs §20)
```

Keep this dependency direction: `Policy`/`Configuration`/`Engine`/`Reporting` may reference
`Evidence` freely; nothing under `CodeRail.Core` should ever need to reference `CodeRail.Cli`.

### Pipeline

`coderail validate` (`CodeRail.Cli/Commands/ValidateCommand.cs`) resolves a `ValidationProfile`
(the embedded `dotnet-default` profile unless `--profile` is given) → `SolutionFileLocator` finds
`.sln`/`.slnx` files under `--path` → `ValidationEngine.RunAsync` runs each profile step's
`IToolExecutor` in order, short-circuiting the remaining steps if `build` fails →
`PolicyEvaluator.Evaluate` turns the collected `ToolResult`s into a `GateResult` →
`IGateResultWriter` renders it (console/json). Exit code: 0 iff `GateResult.Status == Passed` -
this *is* the AI repair-loop contract (docs §16); keep it reliable.

### Adding a new executor

Implement `IToolExecutor` under `Tooling.Executors`, register it in `ValidateCommand.BuildEngine`'s
executor dictionary (keyed by `IToolExecutor.Name`), add its step name to `ValidationSteps`
(`CodeRail.Evidence`), and add a tool id to `ToolIds` if `PolicyEvaluator.EvaluateTool` needs a
dedicated threshold rule for it - otherwise an unrecognized tool id falls back automatically to
"a failed run blocks the gate". Every executor must go through `IProcessRunner`; never call
`Process.Start` directly.

### Scope: which executors exist

Only the four **fast/local** executors from docs §22's MVP list: `build`, `test`, `codeguard`,
`coverage`. `sonar` and `stryker` are deliberately not implemented yet - both involve a remote API
/ long-running mutation testing and aren't needed to prove the core orchestration/evidence loop
(docs §24.6: "prove the execution/evidence model before adding services"). Add them the same way
as `CodeGuardExecutor`/`CoverageExecutor` when needed.

### Known limitations / deliberate simplifications

- **No changed-code awareness** (docs §12). `QualityProfile` thresholds are always
  whole-repository, never "new code only" - needs a `git diff`-based changed-file/changed-project
  resolver that doesn't exist yet. This is the next major feature once the whole-repo gate is
  proven.
- **Executors self-restore/self-build.** `DotnetBuildExecutor`/`DotnetTestExecutor`/
  `CoverageExecutor` run a plain `dotnet build`/`dotnet test` (implicit restore), not the design
  doc's illustrative `--no-restore`/`--no-build` - CodeRail validates arbitrary target
  repositories it has no guarantee were pre-restored by the caller. Known inefficiency:
  `DotnetTestExecutor` and `CoverageExecutor` each redo their own restore+build+test rather than
  sharing one incremental build; acceptable for MVP, worth revisiting once the engine can share
  intermediate output between steps.
- **`CodeGuardExecutor`/`CoverageExecutor` degrade to `PartiallyEvaluated`, not a hard failure**,
  when `codeguard` isn't on PATH, produces non-JSON output (e.g. its own pre-flight rule
  validation failing and printing a plain-text report instead, ignoring `--format`), or no
  coverage report was produced (commonly: the target's test projects don't reference
  `coverlet.collector`). Deliberate - an environment/tooling gap shouldn't block an AI agent's
  repair loop the same way an actual code defect should.

### `ProcessRunner` gotchas (`CodeRail.Execution`)

- **Always sets `MSBUILDDISABLENODEREUSE=1`** on every spawned process by default (a caller-
  supplied value for the same key still wins). Confirmed by direct reproduction: a `dotnet
  build`/`dotnet test` spawned as a child of a VSTest testhost process - i.e. exactly what
  `CodeRail.IntegrationTests` does, and what happens for real whenever this repo's own test suite
  runs - hangs *indefinitely* (not just slow) negotiating with a reusable MSBuild node, while the
  identical command run from an ordinary shell completes in a few seconds. If a `dotnet
  test`/`dotnet build` invocation through `ProcessRunner` ever appears to hang, this is the first
  thing to check.
- Also strips CLR profiler/diagnostics env vars (`CORECLR_PROFILER` etc., `VSTEST_HOST_DEBUG`,
  `DOTNET_STARTUP_HOOKS`) from every child process - defensive, so a profiled/instrumented host
  process (e.g. this repo's own test suite running under coverage) can't leak profiler-attach
  hooks into a child build/test invocation (docs §21, "never expose environment variables
  indiscriminately").
- Caps captured stdout/stderr at 1,000,000 chars per stream (docs §21).
- A timeout is reported via `ProcessResult.TimedOut`, not an exception; caller-initiated
  cancellation (`CancellationToken`) throws `OperationCanceledException` per normal .NET
  convention. Don't conflate the two.

### Fixture-based tests

`tests/CodeRail.Core.Tests/Fixtures/` and `tests/CodeRail.IntegrationTests/Fixtures/` contain
small, real, on-disk .NET projects (including at least one that deliberately fails to compile)
used to exercise executors/the CLI end-to-end against a real `dotnet build`/`dotnet test`. They
are **not** part of `CodeRail.sln` and each has its own `Fixtures/Directory.Build.props` that
shadows the repo root's (MSBuild uses the *nearest* `Directory.Build.props`, not every one it
finds up the tree) - intentional: fixtures must not inherit `TreatWarningsAsErrors`/strict
analysis, and the broken-build fixture is *supposed* to fail to compile. If you add a new
fixture-consuming test project, its `.csproj` needs both `<Compile Remove="Fixtures\**" />` and
`<None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />`, or the fixtures get swept
into that project's own compilation.

### Package version pins

`System.CommandLine` **3.0.0-preview.6.26359.118** (the 3.0 preview API, not the 2.0 beta API most
docs/LLM knowledge covers: `command.SetAction(async (parseResult, ct) => ...)`,
`rootCommand.Subcommands.Add(...)`, `rootCommand.Parse(args).InvokeAsync()`). `YamlDotNet` 18.1.0.
xUnit 2.9.3 + `Microsoft.NET.Test.Sdk` 18.8.1 + `coverlet.collector`/`coverlet.msbuild` 10.0.1
across every test project, matching CodeGuard's pins.
