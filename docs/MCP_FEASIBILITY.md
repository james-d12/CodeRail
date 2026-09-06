# MCP Integration — Feasibility Report

This is the answer to [`HIGH_LEVEL_PLAN_MCP.md`](HIGH_LEVEL_PLAN_MCP.md), which asked whether an
MCP server could be added to "the existing CodeGuard/CodeRail codebase". That brief was written
without access to either repository. This report was written against both.

Every structural claim below cites `file:line` so it stays checkable as the code moves.

---

# 1. Executive Summary

**Adding MCP is feasible, and CodeRail is unusually well-prepared for it — but not at the scope the
brief proposes.**

The brief's proposals split three ways:

| | |
| --- | --- |
| **Buildable now, small effort** | stdio server, `validate_repository`, `list_profiles`, findings drill-down, profile tiering |
| **Buildable, needs prior work** | `review_change` (changed-code awareness reaches only coverage today); artifact retrieval (executors delete their own reports) |
| **Not buildable as described** | a unified CodeGuard+CodeRail server; `engineering://standards` resources; anything Wiz |
| **Should be dropped** | `run_tests`, `fix` |

**Recommended architecture:** a new `src/CodeRail.Mcp` project inside `CodeRail.sln`, surfaced as a
`coderail mcp` subcommand, read-only, stdio-first. This is the brief's "Option D" — none of its
Options A, B or C survive contact with the actual repository layout.

**Precondition:** roughly 40 lines of orchestration currently inlined in a `System.CommandLine`
action lambda (`src/CodeRail.Cli/Commands/ValidateCommand.cs:53-123`) must be extracted into
`CodeRail.Core` first. This is the concrete form of the brief's own "avoid putting business logic
inside MCP tool classes", and it improves the CLI independently of MCP.

**Biggest risk — validation runtime versus MCP tool timeouts.** A `dotnet-thorough` run is build +
test + coverage + Sonar + Stryker. That is minutes to tens of minutes. A blocking `tools/call` will
be abandoned by the client long before it returns. The brief raises long-running operations as one
bullet and then proposes an orchestration that is entirely made of them. §6 gives the mitigation.

**Second risk — concurrency against a shared repository.** CodeRail's executors self-restore and
self-build against the same `obj`/`bin`, and two of them write to fixed output paths. A long-lived
server accepting overlapping requests will collide with itself in ways the one-shot CLI never could.

Nothing here is a blocker. Nothing here is a redesign either — the seams the brief hopes for
already exist, because `HIGH_LEVEL_PLAN.md` reserved a slot for this from the start (§3.2 lists
"MCP/tool integration for AI agents"; §15 says the agent should call `CodeRail.validate()` rather
than individual tools; §23 draws MCP as one of three Agent API surfaces over the same engine). MCP
is a planned surface here, not a new direction.

---

# 2. Critique of the Source Brief

The brief instructed the reader not to assume its architecture and to derive it from the repository
instead. Doing so invalidates a number of its premises.

## 2.1 False premise — CodeGuard and CodeRail are not one codebase

This is the brief's single largest error, and most of its ambitious proposals rest on it.

CodeGuard is a **separate repository** with its own `.git` and its own `CodeGuard.sln`.
`CodeRail.sln` contains only `CodeRail.Evidence`, `CodeRail.Core`, `CodeRail.Cli` and four test
projects. There is no `ProjectReference` or `PackageReference` between them in either direction.

The coupling is a process call against a wire contract:

```text
CodeRailExecutor                 →  codeguard validate --path <repo> --format json
src/CodeRail.Core/Tooling/Executors/CodeGuardExecutor.cs:29
                                    ↓  (resolved off PATH)
CodeGuardJsonParser              →  CodeRail Finding / ValidationStatus
src/CodeRail.Core/Tooling/Parsing/CodeGuardJsonParser.cs
```

And it is deliberate, not incidental. `CodeGuardJsonParser.cs:6-12` states it outright: CodeGuard's
JSON is "treated purely as an external wire contract here, not a shared assembly reference —
'normalise, don't duplicate' (§24.2)".

Consequences:

* **Option A ("MCP inside CodeGuard") is invalid.** CodeGuard would have to reach CodeRail's
  orchestration, which it has no reference to.
* **Option C ("separate MCP project" spanning both) is invalid as drawn.** CodeGuard publishes
  **zero libraries** — `src/CodeGuard.Cli/CodeGuard.Cli.csproj:12-14` is the only `IsPackable=true`
  in that entire solution, and it packs as a tool, not a library. An MCP server cannot reference
  CodeGuard's rule engine without either publishing new packages or merging the repositories.
* **Option B is closest to right**, but needs the modification described in §5.

## 2.2 Wiz does not exist

The brief lists "Potential integrations with security/scanning tooling such as Wiz" as existing
context, and carries "Security" through to its recommended architecture diagram. There is no Wiz
integration, reference, or stub in either repository. It is invented, and should be disregarded.

## 2.3 There is no standards catalogue to expose

The brief's §5 proposes `engineering://standards`, `engineering://standards/dotnet`,
`engineering://standards/ddd` and similar resources, on the assumption that organisation standards
ship with the tooling. They do not.

* CodeGuard packages nothing but its README. There is no embedded docs folder.
* Its rules are **not bundled**. They are resolved at runtime from a user-configured directory or
  git URL (`src/CodeGuard.Cli/Support/RuleSourceResolver.cs`), cached under the OS config root. An
  MCP server cannot assume a rule corpus exists at all.
* The only corpus in-repo is `examples/rules/` — **125 YAML files, every one of them marked
  `illustrative: true`**. They are authoring samples, not an enforced organisational standard.
* At roughly 35-40k tokens for the full corpus, dumping it into an agent's context is self-defeating
  regardless of where it lives. Selective retrieval is the only viable design.

The good news is that the *shape* is right: `RuleDefinition` already carries `Description`,
`Remediation`, `Documentation` refs, `Severity` and `Tags` — genuinely resource-shaped metadata.
The obstacle is distribution, not modelling.

## 2.4 `run_tests` is the anti-pattern the brief itself warns against

The brief's §4 opens with "Do **not** simply expose every CLI command as an MCP tool", then lists
`run_tests` among its proposed capabilities. It is the same mistake, one paragraph later.

It also contradicts `HIGH_LEVEL_PLAN.md` §15, which explicitly prefers `Agent → CodeRail.validate()`
over `Agent → Sonar / Stryker / Coverage / CodeGuard`. And practically: an agent invoking an MCP
server already has a shell with `dotnet test` in it. Exposing it again adds a hop and removes the
evidence normalisation that is CodeRail's entire reason to exist. Drop it.

## 2.5 Long-running operations are underestimated

The brief asks about "long-running operations" in a single §3 bullet, then in §7 proposes
`review_change` as: git diff → affected projects → CodeGuard → build → targeted tests → Sonar →
Stryker.

Sonar alone involves a begin/build/end lifecycle plus remote Web API polling; Stryker is mutation
testing across a solution. `CoverageExecutor` and `DotnetTestExecutor` each redo their own
restore+build+test rather than sharing one (a known inefficiency recorded in `CLAUDE.md`). A
thorough run is not a request-response interaction.

Under MCP spec revision **2026-07-28**, the answer is a task/handle model, not a blocking
`tools/call`. §6 folds this into the design at no extra cost.

## 2.6 Parallel execution is already constrained, and the brief does not know it

The brief's §7 asks "whether operations should run sequentially or in parallel" as though it were
open. It is already settled, and for a reason worth preserving.

`src/CodeRail.Core/Engine/ValidationEngine.cs:19-26` runs **only** `codeguard` concurrently with the
rest of the pipeline, because `test`/`coverage`/`sonar`/`stryker` all self-restore and self-build
against the same `obj`/`bin` under `RepoRoot` — running any two concurrently risks MSBuild file-lock
collisions.

A long-lived server makes this worse in a way a one-shot CLI never exposed. Two overlapping
requests against the same repository would collide not only on MSBuild state but on fixed output
paths:

* `src/CodeRail.Core/Tooling/Executors/StrykerExecutor.cs:67` — writes to a fixed
  `<workingDirectory>/StrykerOutput` and globs the report back recursively.
* `src/CodeRail.Core/Tooling/Executors/SonarExecutor.cs:107` — reads a fixed
  `<repoRoot>/.sonarqube/out/.sonar/report-task.txt`.

Mitigation is a per-repo-root semaphore in the MCP layer (§10), not a Core rewrite.

## 2.7 It never mentions stdout hygiene

The most common way a .NET stdio MCP server breaks is a stray `Console.WriteLine` corrupting the
JSON-RPC frame. The brief's §3 lists "Logging requirements" without naming the hazard.

CodeRail happens to be clean already (§3) — but that is a property worth stating explicitly and
defending in review, not one to discover by accident.

## 2.8 Progressive disclosure has nothing to key on

The brief's §10 is its best section: return a compact verdict, let the agent fetch detail on demand.
Its §4 proposes `explain_finding` / `get_finding` to do the fetching.

Both need a stable finding identifier. `src/CodeRail.Evidence/Finding.cs:7` has none — see §4.1.

## 2.9 Its phase order is backwards

The brief's suggested phases are: (1) minimal stdio server, (2) expose CodeGuard validation,
(3) expose CodeRail execution.

CodeRail is the half that is ready — reusable engine, injectable executors, no console coupling.
CodeGuard is the half that is not — no packable library, no reusable orchestration layer, rules
loaded from an external source, and `MSBuildLocator.RegisterDefaults()` called process-globally and
one-shot at startup (`src/CodeGuard.Cli/Program.cs:10-18`).

Phases 2 and 3 should swap, and CodeGuard should continue to be reached through the process contract
it already has. §12 gives the reordered plan.

## 2.10 The actual precondition goes unmentioned

The brief's §12 says "Avoid putting business logic directly inside MCP tool classes. MCP should
primarily be an adapter." Correct — and it does not notice that the logic an MCP tool would need to
adapt is not currently in an adaptable place. See §4.2.

## 2.11 What the brief gets right

Credit where it is due. These should carry into the implementation:

* **MCP as a thin adapter over existing application capabilities** — achievable here, and cheaply.
* **Asking whether a common finding model already exists** rather than assuming one is needed. It
  does, partially; §4.1 tables the gap against the brief's proposed `EngineeringFinding`.
* **Progressive disclosure as the context-efficiency strategy** (§10). The right instinct, and §6
  shows it costs nothing extra.
* **`assess_test_strength` over `run_stryker`** — the correct altitude, and it maps directly onto
  the fast/medium/thorough tiering that already exists in `examples/profiles/`. The brief guessed
  at an abstraction CodeRail had already built.

---

# 3. Current Architecture — What Makes This Easy

The verdict is "feasible" largely because of properties CodeRail already has.

```text
                    ┌──────────────┐        ┌──────────────┐
                    │ coderail CLI │        │ coderail mcp │   ← proposed second head
                    └──────┬───────┘        └──────┬───────┘
                           │                       │
                           └───────────┬───────────┘
                                       ▼
                          ┌─────────────────────────┐
                          │  ValidationRunner       │   ← to be extracted (§5)
                          │  profile → context → run│
                          └───────────┬─────────────┘
                                      ▼
                          ┌─────────────────────────┐
                          │    ValidationEngine     │
                          └───────────┬─────────────┘
                                      ▼
                    build · test · codeguard · coverage · sonar · stryker
                                      ▼
                          ToolResult / Finding  (Evidence)
                                      ▼
                          PolicyEvaluator → GateResult
                                      ▼
                    IGateResultWriter → console · json · sarif
```

**Zero `System.Console` usage in `CodeRail.Core` or `CodeRail.Evidence`.** Verified by grep across
both projects. Every Console call in `src/` lives in the CLI: `Program.cs:29,35` and
`ValidateCommand.cs:62,120` (all `Console.Error`), plus `ValidateCommand.cs:100` (`Console.Out` as
the default report sink). The library cannot corrupt a JSON-RPC stream.

**Logging is already pinned to stderr.** `src/CodeRail.Cli/Support/CliLoggerFactory.cs:23-24` sets
`LogToStandardErrorThreshold = LogLevel.Trace`, sending *every* level to stderr so that
`--format json` keeps a clean stdout. That is precisely the stdio MCP requirement, arrived at
independently for a different reason.

**No `Environment.Exit` anywhere in `src/` or `tests/`.** Exit codes are returned values
(`ValidateCommand.cs:115`). Nothing will kill a hosting process.

**Reporters take a caller-supplied `TextWriter`** —
`src/CodeRail.Core/Reporting/IGateResultWriter.cs:11`. An MCP tool renders into a `StringWriter` and
gets the payload as a string, with no stdout risk and no new serialization code.
`JsonGateResultWriter` output is usable as an MCP payload nearly as-is (camelCase, string enums;
you would want `WriteIndented = false`).

**`ValidationEngine` is stateless and injectable** —
`src/CodeRail.Core/Engine/ValidationEngine.cs:14-17` takes a plain
`IReadOnlyDictionary<string, IToolExecutor>`, a `PolicyEvaluator` and an `ILogger`. Its only field
is an immutable static `HashSet`. One instance can be built once and shared across requests.

**No ambient current-directory dependency.** `ToolContext.RepoRoot`
(`src/CodeRail.Core/Tooling/IToolExecutor.cs:31`) is an explicit absolute path, and `ProcessRunner`
sets `WorkingDirectory` per child process. A server can validate different repositories
concurrently without `chdir` games.

**`PolicyEvaluator` is pure** — `src/CodeRail.Core/Policy/PolicyEvaluator.cs:13,17`. No I/O, private
static helpers, safe to share.

**No DI container anywhere.** Everything is hand-wired. Adding
`IServiceCollection` registration for an MCP host is greenfield work, not a migration.

---

# 4. Gaps That Must Close First

## 4.1 The evidence model is under-specified for MCP

`src/CodeRail.Evidence/Finding.cs:7`:

```csharp
public sealed record Finding(
    string Type, Severity Severity, string Message, string? File, int? Line, string? RuleId);
```

Against the brief's proposed `EngineeringFinding`:

| Brief's field | Present? | Notes |
| --- | --- | --- |
| `severity`, `file`, `line`, `message`, `rule` | yes | direct equivalents |
| `category` | approximately | `Type` carries it (`"codeguard-violation"`, `"test-failure"`, `"coverage-below-threshold"`, …) |
| **`id`** | **no** | nothing to key `explain_finding` on, nothing to dedupe across repair iterations |
| **`source`** | **no** | recoverable only from the parent `ToolResult.Tool` |
| **`remediation`** | **no** | remediation text is hardcoded English in the console writer, not per-finding data |
| **`metadata`** | **no** | no extensibility bag |

The `source` gap bites hardest. `src/CodeRail.Core/Policy/GateResult.cs:15` exposes
`BlockingFindings` as a **flat `IReadOnlyList<Finding>` with no tool attribution** — so an MCP
response built from it literally cannot tell the agent which tool produced each finding. The
per-tool detail exists in `GateResult.Tools`, but the blocking summary the agent most wants is the
one that has lost it.

Two small additions to `Finding` (a synthesized stable `Id`, and `Source`) close both. Both can be
appended as optional positional parameters without breaking existing construction sites.

`GateResult` also carries no precomputed summary counts — "3 errors, 12 warnings" must be computed
by the consumer.

## 4.2 The orchestration an MCP tool needs is trapped in the CLI

`src/CodeRail.Cli/Commands/ValidateCommand.cs:53-123` is a `System.CommandLine` action lambda that
interleaves genuinely reusable orchestration with things an MCP server must never do:

| Reusable (≈40 lines) | CLI-only, must not leak into MCP |
| --- | --- |
| profile resolution (`:69-71`) | `parseResult.GetValue(...)` reads |
| `ProcessRunner` construction (`:73`) | `Console.Error.WriteLineAsync` (`:62`, `:120`) |
| `SolutionFileLocator.Resolve` (`:75`) | `Console.Out` as report sink (`:100`) |
| `--base-ref` → `ChangeSet` (`:77-85`) | `int` exit-code returns (`:63`, `:115`, `:121`) |
| `SonarConfig` mapping (`:87-89`) | |
| `ToolContext` assembly (`:91`) | |
| engine construction + run (`:94-95`) | |

`BuildEngine` (`:128-141`) is itself clean — it touches no `parseResult`, no Console, no
environment. Its only barrier to reuse is that it is `private static` inside a CLI class.
`CreateWriter` (`:143`) is likewise private.

## 4.3 Smaller items

* **`Artifact` is a dead model.** `src/CodeRail.Evidence/Artifact.cs:5` is never constructed
  anywhere in `src/`; every executor passes `[]`. Worse for MCP, the temp directories holding the
  TRX and Cobertura reports are deleted in a `finally`
  (`DotnetTestExecutor.cs:85-87`, `CoverageExecutor.cs:77-79`). "Fetch me the coverage report" is an
  executor change, not an adapter change.
* **`HttpClient` lifetime.** `ValidateCommand.cs:93` creates one per invocation under `using`.
  Correct for a CLI, wrong for a long-lived server — it wants a single injected client.
* **`GitChangeResolver` throws.** `InvalidOperationException` on an unresolvable base-ref
  (`src/CodeRail.Core/Tooling/GitChangeResolver.cs:42`) or a failed git call (`:71`). An MCP adapter
  must translate these into structured tool errors rather than letting them escape as transport
  faults.
* **Unobserved tasks on cancellation.** `ValidationEngine.cs:88` — if the sequential chain faults or
  cancels, in-flight independent runs are never awaited. Invisible in a process that is about to
  exit; an accumulating leak in one that is not.

---

# 5. Recommended Architecture

Neither Option A, B nor C as written. The brief's Option D — "if the existing codebase suggests a
better approach, propose it":

```text
CodeRail.sln
├── CodeRail.Evidence      unchanged contract assembly (zero dependencies)
├── CodeRail.Core          + ValidationRunner, extracted from the CLI
├── CodeRail.Mcp           NEW — tools, resources, run store, host builder
└── CodeRail.Cli           + `mcp` subcommand (one line in Program.cs)
```

**Why a separate project rather than tools inside the CLI.** They stay unit-testable without going
through `System.CommandLine`, and it preserves the namespace-seam discipline `CLAUDE.md` already
describes — the MCP layer keeps its own boundary rather than accreting into the CLI.

**Why surfaced through the existing CLI rather than a second executable.**
`src/CodeRail.Cli/CodeRail.Cli.csproj:5,13-15` already sets `AssemblyName=coderail`,
`PackAsTool=true` and `ToolCommandName=coderail`. A `mcp` subcommand therefore ships inside the
existing `dotnet tool install -g CodeRail` for **zero new packaging work**, and yields exactly the
client configuration the brief sketched in its §8:

```json
{ "mcpServers": { "coderail": { "command": "coderail", "args": ["mcp"] } } }
```

Wiring it is one line — `Program.cs:12` already does
`rootCommand.Subcommands.Add(ValidateCommand.Build())`; the MCP command is a second such call.

**Precondition refactor.** Extract `ValidationRunner` plus an executor factory from
`ValidateCommand.cs:53-141` into `CodeRail.Core`, taking `ILoggerFactory`, `IProcessRunner` and a
shared `HttpClient`:

```csharp
ValidationRequest(string RepoRoot, string? ProfilePath, string? BaseRef)
    → Task<GateResult>
```

`ValidateCommand` then reduces to parse → run → write → exit code. This is the brief's "MCP is an
adapter, not a home for business logic" made concrete, and it is worth doing on its own merits —
the CLI gets shorter and the orchestration becomes directly testable.

---

# 6. Proposed MCP API

Read-only, CodeRail-only, four tools.

| Tool | Purpose | Existing capability | New code required |
| --- | --- | --- | --- |
| `validate_repository(path, profile?, baseRef?)` | Run the gate; return a **compact verdict plus a run handle** | `ValidationRunner` → `GateResult` | run store; summary projection |
| `get_findings(runId, severity?, tool?, limit, offset)` | Paged drill-down into a completed run | `GateResult.Tools` / `.BlockingFindings` | paging; `Finding.Source` (§4.1) |
| `explain_finding(runId, findingId)` | Full detail for one finding | per-tool `ToolResult` | `Finding.Id` (§4.1) |
| `list_profiles()` | What tiers exist and what each costs | embedded `dotnet-default` + `examples/profiles/` | profile enumeration |

Resources: `coderail://runs/{id}/report.json` (the full `GateResult`, for an agent that genuinely
wants everything) and `coderail://profiles/{name}`.

## 6.1 The central design point

**The run store that progressive disclosure needs is the same run store a long-running-operation
handle needs.** One mechanism answers both of the brief's hardest questions.

`validate_repository` returns immediately with a handle and a compact verdict — the shape the brief
sketched in its §10:

```text
Status: FAILED   (run 7f3a91)

Build       PASS
Tests       PASS
CodeGuard   FAIL
Coverage    PASS

Blocking findings: 2
  1. [codeguard] Domain layer references Infrastructure — OrderService.cs:42
  2. [test]      PaymentServiceTests.RetriesOnTransientFailure failed

Detail: get_findings(runId: "7f3a91")
```

Detail is fetched by `runId` only when the agent needs it. That keeps the common case — a passing
gate during a repair loop — at a few dozen tokens instead of a full report.

Because the handle is CodeRail's own and not protocol machinery, this works with **every** MCP
client regardless of whether it negotiated the Tasks extension. Adopting
`ModelContextProtocol.Extensions.Tasks` later becomes an additive refinement rather than a
prerequisite.

## 6.2 Explicitly rejected

| | Why |
| --- | --- |
| `run_tests` | The anti-pattern the brief itself warns against (§2.4). Agents have a shell. |
| `fix` | CodeRail has no fix capability — nothing in it mutates a repository. Keep the server read-only; that is a property worth defending, not an initial limitation. |
| `get_standards`, `get_rule`, `explain_rule` | CodeGuard-side; see §7. |
| anything Wiz | Does not exist (§2.2). |

## 6.3 Deferred

`review_change` — the brief's headline tool, and genuinely the right altitude. It is buildable on
the existing `--base-ref` path (`GitChangeResolver` → `ChangeSet` → `ToolContext.Changes`), but
`ChangeSet` is today consumed **only** by `CoverageExecutor` and
`CoverageQualityThresholds.NewCodeMinimum`. Until "new issues only" reaches CodeGuard, Sonar and
Stryker findings — already an open item in `CLAUDE.md` — `review_change` would differ from
`validate_repository` in name more than in behaviour. Do that work first, then add the tool.

---

# 7. The CodeGuard Boundary

**Recommendation: keep the process contract exactly as it is.** No cross-repo work, no new packages,
no shared assemblies. MCP exposes CodeGuard findings precisely as CodeRail already normalises them.

This preserves a documented architectural decision (`CodeGuardJsonParser.cs:6-12`) and keeps the
entire MCP effort inside one repository.

What that costs, and one cheap follow-up:

* **CodeRail currently discards data CodeGuard already sends.** CodeGuard's `Violation` record
  carries `Symbol`, `Project`, `Remediation` and `DocumentationReferences`
  (`src/CodeGuard.Core/Results/ValidationResult.cs:29-32`), and `CodeGuardJsonParser` maps none of
  them. Recovering them is a **CodeRail-only** change to an existing parser, and it is exactly what
  would make `explain_finding` worth calling — real remediation text and doc links instead of a
  restated message. Recommended follow-up, not scope.
* **If rule resources are ever wanted:** `codeguard rules list` already supports `--format json`
  (`src/CodeGuard.Cli/Commands/Rules/ListCommand.cs:27`), but `rules explain` has no `--format`
  option at all — text only. That one addition in CodeGuard would make rule resources reachable over
  the same process contract, without publishing any library.
* **Do not host CodeGuard in-process.** `MSBuildLocator.RegisterDefaults()` is process-global and
  one-shot (`src/CodeGuard.Cli/Program.cs:10-18`), and MSBuildWorkspace's memory behaviour across
  repeated analyses is not something to inherit into a long-lived server. The subprocess boundary is
  doing real work here.

---

# 8. Dependencies

| | |
| --- | --- |
| **Package** | `ModelContextProtocol` **2.2.0** (August 2026) |
| **Spec revision** | **2026-07-28** — not 2025-11-25 |
| **Target framework** | supports `net10.0`, matching `Directory.Build.props:4` |
| **Brings in** | `Microsoft.Extensions.Hosting.Abstractions` and `.Caching.Abstractions` ≥ 10.0.10 |

The main package is the right one here: stdio transport, hosting, DI, and attribute-based discovery
(`[McpServerToolType]` / `[McpServerTool]`, `WithStdioServerTransport()`, `WithToolsFromAssembly()`).
Siblings, for completeness: `.Core` (minimum dependencies, client/low-level server only),
`.AspNetCore` (HTTP), `.Extensions.Tasks` (long-running operations), `.Extensions.Apps`.

**Note for anyone working from older material:** SDK 2.0.0 was a breaking restructure — stateless
HTTP by default, discovery-first negotiation, `inputSchema` mandatory on deserialization, and Tasks
extracted into its own package. Tutorials written against 1.x or the 2025-11-25 revision will
mislead. This mirrors the trap `CLAUDE.md` already documents for
`System.CommandLine 3.0.0-preview.6.26359.118`.

**Friction to plan for:** the new dependency lands under `Directory.Build.props:14-15`
(`NuGetAuditLevel=low`, `TreatWarningsAsErrors=true`). `ModelContextProtocol` 2.2.0 is stable, which
is an improvement on the preview `System.CommandLine` reference the repo already tolerates — but the
audit gate is strict and should be checked before committing to the version.

New dependencies belong in `CodeRail.Mcp` only. `CodeRail.Core` currently depends on nothing but
`Microsoft.Extensions.Logging.Abstractions` and `YamlDotNet`, and `CodeRail.Evidence` on nothing at
all. That leanness is worth keeping.

---

# 9. Security

The brief's §9 asks the right question — read-only or mutating — and reaches the right answer.
Worth stating more sharply than it does.

**Read-only by capability, not by policy.** CodeRail has no mutating operation to expose. There is
no `fix`, no write path, nothing that edits a repository. That should stay true.

**The real exposure is one the brief understates.** CodeRail runs `dotnet build` and `dotnet test`
against a target repository — that is **arbitrary MSBuild targets from code the server did not
write**. `HIGH_LEVEL_PLAN.md` §21 already says this ("CodeRail executes arbitrary repository
tooling… it effectively executes untrusted code"). MCP changes the threat model by making that
reachable from a prompt-injected agent rather than only from a developer typing a command.

Concretely, for an MCP server:

* **Validate `path`** against an allowlist of permitted roots, defaulting to the directory the
  server was launched in. An agent must not be able to point `validate_repository` at an arbitrary
  filesystem location and trigger a build there.
* **Never echo environment variables.** The Sonar token stays env-only
  (`SonarExecutor.cs:49`, `CODERAIL_SONAR_TOKEN`) and must never appear in a tool response — note
  that findings and metrics are agent-visible in a way CLI stderr never was.
* **Inherit the existing `ProcessRunner` protections** — timeouts, the 1,000,000-char per-stream
  output cap, CLR profiler/diagnostics variable stripping, and `MSBUILDDISABLENODEREUSE=1`. These
  already exist and already matter more in a server than in a CLI.
* **Serialize per repository root** with a semaphore, for the collision hazard in §2.6.
* **Treat findings as untrusted text.** They contain file paths, messages and rule text originating
  in the analysed repository. They flow into an agent's context; they are data, not instructions.
* **Defer HTTP.** Every consideration above is contained by stdio's single-user, single-process
  model. Introducing HTTP introduces authentication, and there is no demand for it yet (§12).

---

# 10. Testing Strategy

The existing test architecture accommodates this cleanly.

The repo uses **hand-written fakes only** — no Moq, no NSubstitute, no FluentAssertions across all
four test projects. `StubProcessRunner` and `FakeToolExecutor` already exist and are exactly what
MCP tool tests need; a tool test can drive a full `validate_repository` call with no real subprocess.

A `tests/CodeRail.Mcp.Tests` project would cover:

* **Tool discovery and schema contract** — that the advertised tool list and `inputSchema` are what
  clients expect, using the SDK's in-memory client/server pair. This is the test that catches an SDK
  upgrade changing the wire shape.
* **Malformed and hostile inputs** — unknown `runId`, out-of-allowlist `path`, bad `baseRef`
  (which must surface as a tool error, not an `InvalidOperationException` — §4.3).
* **Cancellation and timeout.**
* **Serialization** — that `GateResult` projections round-trip.

**One wrinkle worth flagging.** The CLI and integration tests capture output by mutating
process-global `Console.SetOut`/`Console.SetError`, and both define a `ConsoleOutputCollection`
purely to force sequential execution around that shared state. MCP tests must **not** join that
pattern — the server must never touch `Console` at all, and a test that assumes otherwise would
encode the exact bug §2.7 warns about.

---

# 11. Local Developer Experience

Nothing new to build. Following §5:

```bash
dotnet tool install -g CodeRail    # already how the CLI ships
```

```json
{ "mcpServers": { "coderail": { "command": "coderail", "args": ["mcp"] } } }
```

`scripts/install-local.sh` already handles the local-build install path (it pins the MinVer
prerelease version explicitly so an unpinned `dotnet tool install` cannot silently prefer a stable
nuget.org build) — it needs no changes to cover `coderail mcp`.

Docker and a separate `coderail-mcp` executable are both unnecessary: the former adds no isolation
that matters for a stdio server running as the developer, and the latter duplicates packaging for no
gain.

---

# 12. Staged Plan and Proof of Concept

Reordered from the brief's, for the reasons in §2.9.

| Phase | Work | Notes |
| --- | --- | --- |
| **0** | Extract `ValidationRunner` + executor factory into `CodeRail.Core` | No MCP. CLI behaviour unchanged; existing tests must stay green. Valuable on its own. |
| **1** | Enrich `Finding` (stable `Id`, `Source`); add the run store | Unblocks both drill-down and run handles (§6.1). |
| **2** | `CodeRail.Mcp` + `coderail mcp` stdio; `validate_repository`, `list_profiles` | First working server. |
| **3** | `get_findings`, `explain_finding`; per-repo-root serialization | Completes progressive disclosure. |
| **4** | `review_change` | **Only after** changed-code awareness reaches non-coverage tools (§6.3). |
| **5** | *(optional, cross-repo)* CodeGuard rule resources | Requires `rules explain --format json` in CodeGuard (§7). |
| **6** | *(only on real demand)* HTTP transport | Note SDK 2.0 is stateless-by-default; introduces authentication (§9). |

**Smallest useful proof of concept: phases 0-2, pointed at this repository.** An agent in Claude
Code calls `validate_repository`, gets a compact FAIL verdict back, fixes the finding, calls it
again, gets PASS. That closes the loop `HIGH_LEVEL_PLAN.md` §16 describes, end to end, and proves
the only thing genuinely in doubt — that the evidence model survives the trip to an agent in a form
concise enough to act on.

Not built as part of this investigation, per the brief's constraint #1.

---

# 13. Answer to the Brief's Final Question

> *"Given the current CodeGuard/CodeRail architecture, what is the cleanest and most maintainable
> way to expose our engineering capabilities to AI coding agents through MCP, and what would it take
> to build the first production-quality version?"*

**Cleanest way:** a `CodeRail.Mcp` project inside `CodeRail.sln`, shipped as a `coderail mcp`
subcommand of the existing dotnet tool, exposing four read-only tools over stdio, adapting a
`ValidationRunner` extracted from the CLI. CodeGuard stays behind the process contract it already
has. MCP becomes a second head on the same engine — which is what `HIGH_LEVEL_PLAN.md` §23 always
drew.

**What it would take:** phases 0-3. The work is a small refactor, two fields on a record, a run
store, and an adapter. There is no architectural obstacle, because the properties an MCP server
needs — no console coupling, no ambient state, injectable executors, writer-agnostic reporting —
are already present, mostly for other reasons.

**What it would take that the brief did not anticipate:** the run-handle model, because validation
is too slow for a blocking tool call; per-repo-root serialization, because the executors share build
state; and abandoning the unified CodeGuard+CodeRail server, because they are two repositories with
a deliberate seam between them.
