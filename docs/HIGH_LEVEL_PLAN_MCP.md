# MCP Integration — Feasibility Investigation

## Objective

Investigate the feasibility of adding an **MCP (Model Context Protocol) server** to the existing CodeGuard/CodeRail codebase.

The goal is to allow AI coding agents such as GitHub Copilot, Claude Code, Cursor, etc. to interact with our engineering tooling through MCP.

This is an **investigation only**. Do not implement the feature yet.

The investigation should understand the existing architecture, identify the best integration point, highlight architectural changes required, and produce a recommended implementation approach.

---

## Context

The project currently contains engineering tooling around:

* **CodeGuard** — deterministic engineering/architecture rules engine.
* **CodeRail** — orchestration/execution tooling for engineering checks.
* .NET build/test tooling.
* Static analysis / quality tooling such as Sonar.
* Mutation testing using Stryker.NET.
* Potential integrations with security/scanning tooling such as Wiz.
* Organisation-specific engineering standards and YAML-based rules.

The longer-term goal is to allow an AI coding agent to ask the engineering platform questions such as:

```text
Review my current changes.
```

or:

```text
Validate this repository against our engineering standards.
```

and receive structured, actionable engineering feedback.

The AI should not necessarily need to understand or directly invoke every underlying tool.

---

# Proposed Concept

Investigate introducing an MCP server that exposes higher-level engineering capabilities.

Conceptually:

```text
                     AI Coding Agent
               Copilot / Claude / Cursor
                         │
                         │ MCP
                         ▼
                 ┌─────────────────┐
                 │  CodeRail MCP   │
                 │     Server      │
                 └────────┬────────┘
                          │
             ┌────────────┼─────────────┐
             ▼            ▼             ▼
         CodeGuard      Testing       Analysis
             │            │             │
             │       dotnet test      Sonar
             │       Stryker          Wiz
             │
             ▼
       Engineering Rules
```

Potential MCP capabilities could include:

```text
review_change
validate_repository
run_tests
analyze_change
get_engineering_standards
get_rule
explain_finding
```

These are examples only. Determine the appropriate API based on the existing architecture.

---

# Investigation Areas

## 1. Understand the Existing Architecture

First inspect the entire repository and understand:

* Solution/project structure.
* CodeGuard architecture.
* CodeRail architecture.
* CLI boundaries.
* Core/application/infrastructure layers.
* Existing service abstractions.
* Dependency injection.
* Configuration.
* Execution/orchestration mechanisms.
* Reporting models.
* Existing JSON/SARIF/HTML output.
* Rule loading and validation.
* Test architecture.
* Any existing plugin/extensibility mechanisms.

Do not assume the architecture from this document. Derive it from the actual repository.

---

# 2. Identify the Correct MCP Integration Boundary

Determine where an MCP server should live.

Consider at least:

### Option A — MCP inside CodeGuard

```text
CodeGuard
├── Core
├── CLI
└── MCP
```

### Option B — MCP inside CodeRail

```text
CodeRail
├── Core
├── CLI
└── MCP
```

### Option C — Separate MCP project

```text
CodeGuard
CodeRail
CodeRail.Mcp
```

### Option D — Other architecture

If the existing codebase suggests a better approach, propose it.

For each option evaluate:

* Separation of concerns.
* Dependency direction.
* Reusability.
* Testability.
* Packaging.
* Deployment.
* Local developer experience.
* Future HTTP MCP support.
* Ability to expose both CodeGuard and CodeRail capabilities.
* Whether the architecture allows additional engineering tools later.

Provide a recommendation.

---

# 3. MCP SDK Investigation

Determine the appropriate current MCP SDK/package for .NET.

Investigate:

* Official Microsoft/Model Context Protocol C# SDK.
* Current supported protocol version.
* .NET version compatibility.
* stdio transport.
* Streamable HTTP transport.
* Tool registration/discovery.
* Resources.
* Prompts.
* Authentication/authorization considerations.
* Logging requirements.
* Error handling.
* Cancellation.
* Timeouts.
* Long-running operations.

Do not rely on old MCP tutorials or deprecated APIs.

Identify the exact NuGet packages that would be required.

---

# 4. Tool Design

Investigate what MCP tools should actually be exposed.

Do **not** simply expose every CLI command as an MCP tool.

Consider high-level capabilities such as:

```text
review_change
validate_repository
run_tests
assess_test_strength
get_standards
explain_rule
get_finding
```

For each proposed tool document:

* Name.
* Purpose.
* Inputs.
* Outputs.
* Whether it is synchronous or long-running.
* Required context.
* Expected AI usage.
* Which existing CodeGuard/CodeRail functionality it maps to.
* Whether it should exist at all.

Pay particular attention to the distinction between:

```text
run_stryker
```

and:

```text
assess_test_strength
```

The latter may be a better abstraction because CodeRail can decide when Stryker is appropriate.

---

# 5. Context and MCP Resources

Investigate whether organisation engineering standards should be exposed through MCP resources rather than tools.

Potential resources:

```text
engineering://standards
engineering://standards/dotnet
engineering://standards/ddd
engineering://standards/events
engineering://architecture
engineering://repository
```

Determine whether the existing YAML/Markdown standards infrastructure can naturally support this.

Investigate:

* How rules are currently loaded.
* Whether rules can be retrieved selectively.
* How large the context could become.
* Whether resources should be dynamically generated.
* Whether the AI should retrieve rules based on the current repository/change.

---

# 6. Unified Engineering Results

Investigate whether existing outputs from:

* CodeGuard
* dotnet build
* dotnet test
* Stryker.NET
* Sonar
* Wiz
* other tools

can be represented using a common internal model.

For example:

```text
EngineeringFinding

- id
- source
- severity
- category
- file
- line
- message
- rule
- remediation
- metadata
```

Determine whether such a model already exists.

If not, determine whether introducing one would be beneficial.

The objective is for the MCP layer to return **concise, structured engineering feedback**, rather than dumping raw CLI output into the AI context.

---

# 7. Orchestration

Investigate how a tool such as:

```text
review_change
```

could orchestrate multiple checks.

For example:

```text
review_change
      │
      ├── inspect git diff
      │
      ├── identify affected projects
      │
      ├── CodeGuard
      │
      ├── build
      │
      ├── targeted tests
      │
      ├── Sonar
      │
      └── Stryker
```

Determine:

* What orchestration capabilities already exist.
* Whether CodeRail already provides the appropriate abstraction.
* Which checks should be automatic.
* Which checks should be optional.
* How expensive operations such as mutation testing should be handled.
* Whether operations should run sequentially or in parallel.
* How failures should be represented.
* How cancellation should work.

---

# 8. Local Developer Experience

Investigate how a developer would install and configure the MCP server.

For example:

```text
dotnet tool install ...
```

followed by an MCP configuration similar to:

```json
{
  "mcpServers": {
    "coderail": {
      "command": "coderail",
      "args": ["mcp"]
    }
  }
}
```

Determine the most natural approach based on the existing CLI/package architecture.

Consider:

* Global .NET tool.
* Executable bundled with CodeRail.
* Separate `coderail-mcp` executable.
* Docker.
* Local stdio server.
* Future HTTP server.

Do not implement this yet.

---

# 9. Security Considerations

MCP tools may execute code against a developer's repository.

Investigate:

* What commands MCP tools could execute.
* Arbitrary command execution risks.
* Repository path validation.
* Working directory restrictions.
* Environment variable exposure.
* Secrets exposure.
* Tool permissions.
* Authentication if HTTP is introduced.
* Prompt/tool injection risks.
* Whether destructive operations should be exposed.

Particularly assess whether tools should be:

```text
read-only
```

versus:

```text
mutating
```

For example, whether MCP should ever expose:

```text
fix
```

or whether MCP should initially remain read-only.

---

# 10. AI Context Efficiency

A core requirement is avoiding unnecessary context consumption.

Investigate how MCP responses should be designed so that:

```text
review_change()
```

returns something like:

```text
Status: FAILED

Checks:
Build       PASS
Tests       PASS
CodeGuard   FAIL
Stryker     WARNING

Findings: 2

1. Domain references Infrastructure
2. Mutation survived in PaymentService.cs
```

The AI should then be able to retrieve additional details when necessary.

Investigate whether MCP resources/tools can support this progressive-disclosure model.

---

# 11. Testing Strategy

Determine how the MCP layer would be tested.

Consider:

* Unit tests for tool implementations.
* Integration tests against the MCP protocol.
* Contract tests.
* Tests using an MCP client.
* Tests for malformed inputs.
* Cancellation tests.
* Timeout tests.
* Tool discovery tests.
* Serialization tests.

Determine whether the existing test architecture can accommodate this cleanly.

---

# 12. Backwards Compatibility

The existing CLI must continue to work independently of MCP.

The desired architecture should ideally allow:

```text
CLI ────────────────┐
                    │
MCP ────────────────┼──> Core/Application
                    │
Future API ─────────┘
```

Avoid putting business logic directly inside MCP tool classes.

MCP should primarily be an adapter/interface over existing application capabilities.

---

# Deliverables

Produce a feasibility report containing:

## 1. Executive Summary

Is adding MCP feasible?

What is the recommended architecture?

What are the major risks?

---

## 2. Current Architecture

Describe the relevant existing architecture discovered in the repository.

Include a simple diagram.

---

## 3. Recommended MCP Architecture

Show:

```text
AI Agent
   │
  MCP
   │
CodeRail MCP
   │
   ├── CodeGuard
   ├── Testing
   ├── Analysis
   └── Security
```

Adapt this to the actual architecture discovered.

---

## 4. Proposed MCP API

Provide a table:

| Tool/Resource       | Purpose | Existing Capability | New Code Required |
| ------------------- | ------- | ------------------- | ----------------- |
| review_change       | ...     | ...                 | ...               |
| validate_repository | ...     | ...                 | ...               |
| get_standards       | ...     | ...                 | ...               |

Only recommend tools that have a clear purpose.

---

## 5. Dependency Changes

List:

* NuGet packages.
* New projects.
* Existing projects requiring modification.
* Configuration changes.
* Packaging changes.

---

## 6. Implementation Plan

Provide a staged plan.

Prefer something similar to:

### Phase 1

Minimal read-only stdio MCP server.

### Phase 2

Expose CodeGuard validation.

### Phase 3

Expose CodeRail execution/testing.

### Phase 4

Add engineering resources/standards.

### Phase 5

Add higher-level `review_change` orchestration.

### Phase 6

Investigate HTTP/self-hosted MCP.

Adapt these phases to the actual repository.

---

## 7. Proof of Concept

Describe the smallest useful POC.

The POC should ideally demonstrate:

```text
AI Agent
   ↓
MCP
   ↓
CodeGuard
   ↓
structured findings
   ↓
AI understands/fixes finding
```

Do not implement the POC during this investigation unless explicitly requested.

---

# Important Constraints

1. **Do not implement the MCP server yet.**
2. Do not blindly follow the proposed architecture.
3. Inspect the existing codebase first.
4. Be critical about whether MCP belongs in CodeGuard, CodeRail, or a separate project.
5. Do not duplicate existing business logic.
6. Prefer adapters around existing application services.
7. Do not expose raw CLI commands merely because they exist.
8. Prefer high-level AI-oriented capabilities where appropriate.
9. Consider MCP's current 2026 specification and .NET SDK rather than historical examples.
10. Identify anything in the existing architecture that would make MCP integration difficult.
11. Explicitly call out architectural problems or refactoring that should happen before MCP is introduced.

# Final Question

The investigation should ultimately answer:

> **"Given the current CodeGuard/CodeRail architecture, what is the cleanest and most maintainable way to expose our engineering capabilities to AI coding agents through MCP, and what would it take to build the first production-quality version?"**
