# CodeRail

**CodeRail** is a deterministic quality-validation and orchestration layer for AI-assisted
software development.

It does not replace existing engineering tools such as [SonarCloud](https://www.sonarsource.com/products/sonarcloud/),
[Stryker](https://stryker-mutator.io/), coverage tooling, `dotnet test`, or
[CodeGuard](https://github.com/james-d12/CodeGuard). Instead it executes them, normalises their
results into a common evidence model, applies configurable quality policies, and hands an AI
coding agent a single deterministic PASS/FAIL verdict with actionable findings — so the agent can
iteratively repair a change until the quality gate passes, instead of declaring itself done.

See [`docs/HIGH_LEVEL_PLAN.md`](docs/HIGH_LEVEL_PLAN.md) for the full design.

## Status

MVP — not yet published as a NuGet tool. The CLI surface is a single command:

```bash
dotnet run --project src/CodeRail.Cli -- validate --path <repo>
```

which runs `dotnet build`, `dotnet test`, [CodeGuard](https://github.com/james-d12/CodeGuard), and
coverage collection against the target repository (skipping any step it has no evidence for -
e.g. CodeGuard not being installed - rather than crashing), applies a quality profile, and prints
(or, with `--format json`, emits) a pass/fail gate result. Exit code 0 means the gate passed.

```bash
# override the built-in default profile, emit JSON instead of console output
dotnet run --project src/CodeRail.Cli -- validate --path <repo> --profile my-profile.yml --format json
```

Sonar and Stryker executors aren't implemented yet - see [`CLAUDE.md`](CLAUDE.md) for the current
scope and how to add one.

## Building

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```
