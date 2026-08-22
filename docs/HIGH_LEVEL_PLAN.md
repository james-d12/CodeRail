# CodeRail — High-Level Design

## 1. Overview

**CodeRail** is a deterministic quality-validation and orchestration layer for AI-assisted software development.

CodeRail does **not** attempt to replace established engineering tools such as SonarCloud, Stryker, code coverage tools, `dotnet test`, or CodeGuard.

Instead, it provides a lightweight integration layer that:

1. Executes existing engineering tools.
2. Collects their results.
3. Normalises those results into a common evidence model.
4. Applies configurable quality policies.
5. Produces concise, machine-readable feedback for AI coding agents.
6. Allows an AI agent to iteratively repair a change until the quality gate passes.

The central principle is:

> **AI agents generate code; deterministic tools provide evidence; CodeRail decides whether the change is acceptable.**

---

# 2. Problem

AI coding agents are increasingly capable of implementing substantial pieces of software autonomously.

However, an agent saying:

> "The implementation is complete."

does not provide sufficient evidence that the implementation is actually acceptable.

A typical organisation may already have many engineering controls:

* unit tests
* build validation
* code coverage
* SonarCloud
* Stryker
* security scanners
* CodeGuard
* architectural rules
* organisation-specific coding standards

The problem is that these tools operate independently.

Their output may be:

* console logs
* XML
* JSON
* SARIF
* HTML reports
* remote API results
* CI pipeline results

An AI agent should not need to understand each tool individually.

It should instead receive a concise answer:

```text
QUALITY GATE: FAILED

Build: PASS
Tests: PASS
CodeGuard: FAIL
Coverage: PASS
Sonar: PASS
Stryker: FAIL

Blocking findings:
- Domain layer references Infrastructure.
- 3 surviving mutations in PaymentService.

Action required:
Fix the architecture violation and improve tests.
```

CodeRail provides this missing layer.

---

# 3. Goals

## 3.1 Primary goals

CodeRail should:

* provide a common interface for engineering validation tools
* execute existing tools rather than reimplementing them
* normalise heterogeneous tool output
* support deterministic quality gates
* distinguish blocking and informational findings
* provide machine-readable results for AI agents
* support iterative AI repair loops
* support changed-code validation
* support local and CI execution
* remain independent of a specific AI vendor or IDE
* be easy to extend with new tools

## 3.2 Secondary goals

CodeRail should eventually support:

* VS Code integration
* MCP/tool integration for AI agents
* GitHub Actions
* Azure DevOps
* containerised execution
* organisation-specific quality profiles
* historical validation data
* dashboards and reporting

These are explicitly not required for the initial MVP.

---

# 4. Non-goals

CodeRail should **not** become:

* a replacement for SonarCloud
* a replacement for Stryker
* a replacement for coverage tooling
* a replacement for `dotnet test`
* a general-purpose CI/CD platform
* a new static analysis engine
* a new mutation testing engine
* an IDE
* an AI coding agent

CodeRail should remain a relatively thin orchestration and evidence layer.

---

# 5. Conceptual Architecture

```text
                         ┌───────────────────┐
                         │    AI Agent       │
                         │                   │
                         │ Orchestrator      │
                         │ Coding Agent      │
                         └─────────┬─────────┘
                                   │
                              validate()
                                   │
                                   ▼
                         ┌───────────────────┐
                         │     CodeRail      │
                         │                   │
                         │ Validation Engine │
                         └─────────┬─────────┘
                                   │
                 ┌─────────────────┼─────────────────┐
                 │                 │                 │
                 ▼                 ▼                 ▼
          ┌────────────┐    ┌────────────┐    ┌────────────┐
          │ CodeGuard  │    │ SonarCloud │    │  Stryker   │
          └────────────┘    └────────────┘    └────────────┘
                 │                 │                 │
                 └─────────────────┼─────────────────┘
                                   │
                                   ▼
                         ┌───────────────────┐
                         │ Evidence Model    │
                         │                   │
                         │ Findings          │
                         │ Metrics           │
                         │ Status            │
                         │ Artifacts         │
                         └─────────┬─────────┘
                                   │
                                   ▼
                         ┌───────────────────┐
                         │ Quality Policy    │
                         │                   │
                         │ PASS / FAIL       │
                         └─────────┬─────────┘
                                   │
                            ┌──────┴──────┐
                            │             │
                           PASS          FAIL
                            │             │
                            ▼             ▼
                          Done       AI Repair Loop
```

---

# 6. Core Components

## 6.1 Validation Engine

The Validation Engine coordinates the execution of validation steps.

Example pipeline:

```yaml
validation:
  steps:
    - build
    - test
    - codeguard
    - coverage
    - sonar
    - stryker
```

The engine:

1. Loads the validation profile.
2. Determines applicable tools.
3. Executes tools in dependency order.
4. Collects results.
5. Applies quality policies.
6. Produces the final validation result.

The engine should support short-circuiting.

For example:

```text
Build FAIL
    ↓
Do not run Stryker
    ↓
Return failure
```

There is little value in performing expensive analysis when the project does not compile.

---

# 7. Tool Executors

Each external tool is represented by an executor.

```csharp
public interface IToolExecutor
{
    string Name { get; }

    Task<ToolResult> ExecuteAsync(
        ToolContext context,
        CancellationToken cancellationToken);
}
```

Initial implementations:

```text
DotnetBuildExecutor
DotnetTestExecutor
CoverageExecutor
CodeGuardExecutor
SonarExecutor
StrykerExecutor
```

The executor is responsible for:

1. determining how the tool should be invoked
2. executing the tool
3. capturing output
4. locating generated reports
5. parsing the results
6. converting the results into the common evidence model

---

# 8. Process Execution

Most tools will initially be invoked through existing CLIs.

CodeRail should provide a small process abstraction:

```csharp
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}
```

For example:

```text
CodeGuardExecutor
       │
       ▼
ProcessRunner
       │
       ▼
codeguard validate --format json
       │
       ▼
codeguard.json
       │
       ▼
CodeGuardResultParser
```

This keeps tool-specific execution details isolated.

The ProcessRunner should eventually support:

* cancellation
* timeout
* environment variables
* stdout/stderr capture
* exit codes
* output limits
* process-tree termination
* command logging
* execution duration

---

# 9. Tool Examples

## 9.1 Build

CodeRail executes:

```bash
dotnet build --no-restore
```

The result is converted into:

```json
{
  "tool": "dotnet-build",
  "status": "passed"
}
```

---

## 9.2 Tests

CodeRail executes:

```bash
dotnet test --no-build --logger trx
```

The TRX output is parsed into:

```json
{
  "tool": "dotnet-test",
  "status": "passed",
  "metrics": {
    "total": 184,
    "passed": 184,
    "failed": 0,
    "skipped": 0
  }
}
```

---

## 9.3 Coverage

Initially CodeRail can use the .NET coverage collector:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

The generated coverage report is parsed into metrics such as:

```json
{
  "tool": "coverage",
  "status": "passed",
  "metrics": {
    "lineCoverage": 91.2,
    "branchCoverage": 84.3
  }
}
```

---

## 9.4 CodeGuard

CodeRail executes:

```bash
codeguard validate --format json
```

CodeGuard already provides deterministic organisation-specific rules, making it a natural first-class CodeRail provider.

Example:

```json
{
  "tool": "codeguard",
  "status": "failed",
  "findings": [
    {
      "severity": "error",
      "rule": "domain-must-not-reference-infrastructure",
      "file": "OrderService.cs",
      "line": 42
    }
  ]
}
```

---

## 9.5 Stryker

CodeRail invokes Stryker through its normal .NET tooling.

Conceptually:

```bash
dotnet stryker
```

The executor consumes Stryker's machine-readable output and converts it into:

```json
{
  "tool": "stryker",
  "status": "failed",
  "metrics": {
    "mutationScore": 72.3,
    "killed": 142,
    "survived": 37
  }
}
```

Surviving mutations become actionable findings.

---

## 9.6 SonarCloud

SonarCloud is different because validation involves both local execution and a remote service.

The executor performs the Sonar analysis lifecycle:

```text
begin analysis
      ↓
dotnet build
      ↓
dotnet test
      ↓
end analysis
      ↓
wait for analysis
      ↓
SonarCloud API
      ↓
retrieve issues/metrics
```

The executor hides this complexity from the rest of CodeRail.

The final output is still a normal `ToolResult`.

---

# 10. Common Evidence Model

The most important architectural abstraction is the common evidence model.

A simplified result:

```csharp
public sealed record ToolResult(
    string Tool,
    ValidationStatus Status,
    IReadOnlyList<Finding> Findings,
    IReadOnlyDictionary<string, object> Metrics,
    IReadOnlyList<Artifact> Artifacts);
```

A finding might contain:

```csharp
public sealed record Finding(
    string Type,
    Severity Severity,
    string Message,
    string? File,
    int? Line,
    string? RuleId);
```

This allows the AI layer to consume:

```text
CodeGuard finding
Sonar finding
Stryker finding
Coverage finding
```

using the same structure.

The native output format of each underlying tool remains an implementation detail.

---

# 11. Quality Policies

Execution and policy evaluation should be separate concepts.

A tool can return:

```text
Stryker:
mutation score = 72%
```

The policy determines whether:

```text
72% = PASS
```

or:

```text
72% = FAIL
```

Example:

```yaml
quality:
  coverage:
    newCodeMinimum: 85

  mutation:
    newCodeMinimum: 75

  sonar:
    newBlocker: 0
    newCritical: 0

  codeguard:
    errorCount: 0
    criticalCount: 0
```

This makes the tools reusable across different organisations and repositories.

---

# 12. Changed-Code Awareness

A critical feature is distinguishing existing problems from problems introduced by the AI.

CodeRail should determine:

```text
base revision
     ↓
git diff
     ↓
changed files
     ↓
changed projects
     ↓
changed code
```

Quality gates can then focus on new code.

Example:

```text
Existing coverage: 72%

New code coverage: 94%

Result: PASS
```

Similarly:

```text
Existing Sonar issues: 143
New Sonar issues: 0

Result: PASS
```

This prevents AI agents from being forced to fix an entire legacy codebase before completing a small story.

---

# 13. Validation Profiles

Repositories should be able to define profiles.

Example:

```yaml
profile: dotnet-default

validation:
  - build
  - test
  - codeguard
  - coverage
  - sonar
  - stryker
```

Different profiles could eventually exist:

```text
dotnet-default
dotnet-fast
dotnet-production
security-critical
library
service
```

The profile controls:

* enabled tools
* ordering
* thresholds
* blocking behaviour
* expensive checks

---

# 14. Fast vs Expensive Validation

Not every check should run after every AI iteration.

A sensible default would be:

### Fast loop

```text
build
test
CodeGuard
```

### Medium loop

```text
coverage
Sonar
```

### Final validation

```text
Stryker
security analysis
full integration tests
```

Example:

```text
AI implementation
      ↓
Fast validation
      ↓
FAIL → repair
      ↓
PASS
      ↓
Medium validation
      ↓
FAIL → repair
      ↓
PASS
      ↓
Expensive validation
      ↓
PASS
      ↓
Complete
```

This keeps autonomous development reasonably fast.

---

# 15. AI Agent Integration

The initial integration should be deliberately simple.

The agent can invoke:

```bash
coderail validate --format json
```

The JSON result becomes the agent's feedback.

Eventually CodeRail can expose a tool interface such as:

```text
coderail.validate()
coderail.status()
coderail.findings()
```

The agent should **not need to understand the individual underlying tools**.

Prefer:

```text
Agent → CodeRail.validate()
```

over:

```text
Agent → Sonar
Agent → Stryker
Agent → Coverage
Agent → CodeGuard
```

This allows CodeRail to control the validation workflow.

---

# 16. AI Repair Loop

The intended workflow is:

```text
1. Agent plans
2. Agent implements
3. Agent runs CodeRail
4. CodeRail executes validation
5. CodeRail returns evidence
6. Agent fixes blocking findings
7. Agent runs CodeRail again
8. Repeat until PASS
```

The agent should never be the final authority on whether its work is complete.

CodeRail provides the deterministic decision.

---

# 17. Execution Environments

The initial MVP can execute tools directly on the host.

However, tool availability must eventually be treated as an explicit concern.

Potential execution modes:

```text
LocalProcessExecutor
ContainerExecutor
CIExecutor
```

For example:

```text
Developer machine
    ↓
CodeRail
    ↓
local tools
```

versus:

```text
CI agent
    ↓
CodeRail
    ↓
container
    ↓
known tool versions
```

Containerised execution provides reproducibility and prevents "works on my machine" problems.

This should be introduced after the basic executor architecture is proven.

---

# 18. Developer Experience

VS Code should not be treated as the core of CodeRail.

Existing tools already provide excellent IDE integrations.

Instead, CodeRail should initially operate as a CLI and agent-facing service.

A future VS Code extension could expose:

```text
CodeRail
──────────────────────────

Story: Add payment retries

✓ Build
✓ Tests
✓ CodeGuard
✓ Coverage       91%
✓ Sonar
✗ Stryker        3 mutations

Overall: FAILED

[View Findings]
[Ask Agent to Fix]
[Run Again]
```

The extension is therefore a presentation/control surface rather than the execution engine.

---

# 19. CI/CD Integration

CodeRail should eventually be usable as a normal CI tool.

### GitHub Actions

```yaml
- name: CodeRail
  run: coderail validate --format sarif
```

### Azure DevOps

```yaml
- script: coderail validate --format sarif
  displayName: CodeRail
```

The same validation engine should be usable:

```text
AI Agent
   │
   ▼
CodeRail

GitHub Actions
   │
   ▼
CodeRail

Azure DevOps
   │
   ▼
CodeRail
```

This avoids maintaining separate validation implementations.

---

# 20. Output Formats

CodeRail should support at least:

```text
console
json
sarif
```

### Console

Human-readable developer output.

### JSON

Primary machine-readable format for AI agents.

### SARIF

Integration with existing code-scanning systems.

Potential future formats:

```text
JUnit
HTML
OpenTelemetry
```

---

# 21. Security Considerations

CodeRail executes arbitrary repository tooling.

This means it effectively executes untrusted code.

This is particularly important because AI agents may modify build scripts, project files, package references, or tooling configuration.

Therefore:

* avoid executing untrusted repositories with excessive privileges
* support container isolation
* limit filesystem access where possible
* limit network access where possible
* protect secrets such as Sonar tokens
* never expose environment variables indiscriminately
* enforce execution timeouts
* capture and restrict excessive process output

For CI execution, container isolation should eventually be the preferred model.

---

# 22. MVP

The MVP should be intentionally small.

### Core

* .NET CLI
* `coderail validate`
* `IProcessRunner`
* `IToolExecutor`
* common `ToolResult`
* validation pipeline
* YAML configuration
* JSON output

### Initial executors

1. `dotnet build`
2. `dotnet test`
3. CodeGuard
4. coverage
5. SonarCloud
6. Stryker

### MVP workflow

```text
coderail validate
       ↓
build
       ↓
test
       ↓
CodeGuard
       ↓
coverage
       ↓
Sonar
       ↓
Stryker
       ↓
quality policy
       ↓
JSON result
```

No dashboard.

No VS Code extension.

No hosted service.

No distributed execution.

No AI-specific SDK dependency.

The goal is to prove that the **validation/evidence abstraction works**.

---

# 23. Future Evolution

Once the core loop is working, CodeRail can evolve into:

```text
                    CodeRail
                       │
       ┌───────────────┼────────────────┐
       │               │                │
   Validation       Execution         Agent API
       │               │                │
       │               │                ├── MCP
       │               │                ├── CLI
       │               │                └── HTTP
       │               │
       │               ├── Local
       │               ├── Docker
       │               └── Kubernetes
       │
       ├── CodeGuard
       ├── Sonar
       ├── Stryker
       ├── Coverage
       ├── Security
       └── Custom tools
                       │
                       ▼
                  Evidence Store
                       │
                       ▼
                    Dashboard
```

This could eventually provide historical insights such as:

* which agents produce the most failed validations
* which models require the most repair iterations
* which coding standards are frequently violated
* mutation-score trends
* coverage trends
* common AI-generated defects
* average validation iterations per story
* cost per successfully completed story

---

# 24. Architectural Principles

The following principles should guide implementation.

### 1. Reuse, don't replace

CodeRail should leverage existing engineering tools.

### 2. Normalize, don't duplicate

Each tool retains its native implementation and output; CodeRail converts it into common evidence.

### 3. Policy is separate from execution

A tool reports facts. CodeRail policy determines whether those facts are acceptable.

### 4. AI is not the judge

The AI can propose and repair code, but deterministic validation determines completion.

### 5. Keep the core editor-agnostic

VS Code is an important client, not the platform itself.

### 6. Start with a CLI

Prove the execution/evidence model before adding services and integrations.

### 7. Make expensive validation intentional

Fast feedback should happen frequently; expensive analysis should happen at appropriate gates.

### 8. Prefer structured output

JSON should be the primary interface between CodeRail and AI agents.

---

# 25. Summary

CodeRail should be a **thin deterministic control layer over the existing software-engineering toolchain**.

It does not compete with SonarCloud, Stryker, coverage tooling, or CodeGuard.

Instead:

```text
                    AI Agent
                       │
                       ▼
                   CodeRail
                       │
        ┌──────────────┼──────────────┐
        ▼              ▼              ▼
    CodeGuard        Sonar         Stryker
        │              │              │
        └──────────────┼──────────────┘
                       ▼
                    Evidence
                       │
                       ▼
                  Quality Policy
                       │
                  ┌────┴────┐
                  │         │
                 PASS      FAIL
                  │         │
                  ▼         ▼
                Done      Repair
                            │
                            └───────► AI
```

The core value proposition is therefore not:

> **"We built another code analysis tool."**

It is:

> **"We give autonomous coding agents a deterministic quality-control loop."**

CodeGuard remains the organisation-specific rules engine, while CodeRail becomes the orchestration and evidence layer that brings CodeGuard together with the existing engineering ecosystem.
