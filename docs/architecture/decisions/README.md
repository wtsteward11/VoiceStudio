# Architecture Decision Records (ADRs)

This directory contains Architecture Decision Records for VoiceStudio.

## What is an ADR?

An ADR documents a significant architectural decision along with its context and consequences.

## When to create an ADR

Per `.cursor/rules/core/architecture.mdc`, an ADR is required when:

- Adding or removing a major dependency
- Changing project structure
- Changing engine integration strategy
- Introducing persistence schemas or migrations

## ADR Format

Use this template:

```markdown
# ADR-NNN: Title

## Status
[Proposed | Accepted | Deprecated | Superseded by ADR-NNN]

## Context
What is the issue that we're seeing that is motivating this decision or change?

## Options Considered
1. **Option A**: Description
   - Pros: ...
   - Cons: ...

2. **Option B**: Description
   - Pros: ...
   - Cons: ...

## Decision
What is the change that we're proposing and/or doing?

## Consequences
What becomes easier or more difficult to do because of this change?
```

## Naming Convention

- Format: `ADR-NNN-short-title.md`
- Example: `ADR-001-engine-subprocess-isolation.md`
- Numbers are sequential and never reused

## Index

### ADR Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [ADR-001](ADR-001-rulebook-integration.md) | Cursor Agent Rulebook Integration | Accepted | 2026-01-25 |
| [ADR-002](ADR-002-document-governance.md) | Document Governance Architecture | Accepted | 2026-02-04 |
| [ADR-003](ADR-003-agent-governance-framework.md) | Agent Governance Framework | Accepted | 2026-01-25 |
| [ADR-004](ADR-004-messagepack-ipc.md) | MessagePack IPC Transport | Superseded | 2026-01-30 |
| [ADR-005](ADR-005-context-management.md) | Context Management System | Accepted | 2026-02-04 |
| [ADR-006](ADR-006-cursor-rules-system.md) | Enhanced Cursor Rules System | Accepted | 2026-02-04 |
| [ADR-007](ADR-007-ipc-boundary.md) | IPC Boundary (Control vs Data Plane) | Accepted | 2026-01-30 |
| [ADR-008](ADR-008-architecture-patterns.md) | Architecture Patterns and Enforcement | Accepted | 2026-02-04 |
| [ADR-009](ADR-009-ai-native-development.md) | AI-Native Development Patterns | Accepted | 2026-02-04 |
| [ADR-010](ADR-010-native-windows-platform.md) | Native Windows Platform | Accepted | 2026-01-30 |
| [ADR-011](ADR-011-context-manager-architecture.md) | Context Manager Architecture | Accepted | 2026-02-04 |
| [ADR-012](ADR-012-roadmap-integration.md) | Roadmap Integration Scaffolding | Accepted | 2026-02-04 |
| [ADR-013](ADR-013-opentelemetry-tracing.md) | OpenTelemetry Distributed Tracing | Accepted | 2026-02-04 |
| [ADR-014](ADR-014-agent-skills.md) | Agent Skills Integration | Accepted | 2026-02-04 |
| [ADR-015](ADR-015-architecture-integration-contract.md) | Architecture Integration Contract | Accepted | 2026-01-28 |
| [ADR-016](ADR-016-gate-c-artifact-choice.md) | Gate C Artifact Choice | Accepted | 2026-01-29 |
| [ADR-017](ADR-017-engine-subprocess-model.md) | Engine Subprocess Model | Accepted | 2026-02-04 |
| [ADR-018](ADR-018-named-pipes-http.md) | Named Pipes Replaced with HTTP | Accepted | 2026-01-30 |
| [ADR-019](ADR-019-orchestration-in-python.md) | Orchestration in Python | Accepted | 2026-01-30 |
| [ADR-020](ADR-020-debug-role-architecture.md) | Debug Role Architecture | Accepted | 2026-02-10 |
| [ADR-021](ADR-021-voice-ai-pipeline.md) | Voice AI Pipeline Architecture | Accepted | 2026-02-10 |
| [ADR-022](ADR-022-ddd-bounded-contexts.md) | DDD Bounded Contexts | Accepted | 2026-02-10 |
| [ADR-023](ADR-023-ui-assembly-split.md) | UI Assembly Split into Feature Modules | Accepted | 2026-02-01 |
| [ADR-024](ADR-024-completion-evidence-guard.md) | Completion Evidence Guard | Accepted | 2026-02-01 |
| [ADR-025](ADR-025-compatibility-matrix-and-scaffolding.md) | Compatibility Matrix and Scaffolding | Accepted | 2026-02-02 |
| [ADR-026](ADR-026-infrastructure-remediation.md) | Infrastructure Remediation | Accepted | 2026-02-02 |
| [ADR-027](ADR-027-unified-verification-harness.md) | Unified Verification Harness | Accepted | 2026-02-09 |
| [ADR-028](ADR-028-unified-command-architecture.md) | Unified Command Architecture | Accepted | 2026-02-08 |
| [ADR-029](ADR-029-hybrid-supervisor.md) | Hybrid Supervisor Architecture | Accepted | 2026-02-09 |
| [ADR-030](ADR-030-viewmodel-di-migration.md) | ViewModel Dependency Injection Migration | Accepted | 2026-02-06 |
| [ADR-031](ADR-031-api-versioning-strategy.md) | API Versioning Strategy | Accepted | 2026-02-10 |
| [ADR-032](ADR-032-middleware-stack.md) | Middleware Stack Architecture | Accepted | 2026-02-10 |
| [ADR-033](ADR-033-config-consolidation.md) | Config Consolidation | Accepted | 2026-02 |
| [ADR-034](ADR-034-enhanced-engine-routing.md) | Enhanced Engine Routing | Accepted | 2026-02 |
| [ADR-035](ADR-035-sentinel-deterministic-workflow.md) | Sentinel Deterministic Workflow | Accepted | 2026-02 |
| [ADR-036](ADR-036-plugin-system-unification.md) | Plugin System Unification | Accepted | 2026-02 |
| [ADR-037](ADR-037-plugin-trust-lane-model.md) | Plugin Trust Lane Model | Accepted | 2026-02 |
| [ADR-038](ADR-038-plugin-abc-unification.md) | Plugin ABC Unification | Accepted | 2026-02 |
| [ADR-039](ADR-039-phase6-strategic-maturity.md) | Phase 6 Strategic Maturity | Accepted | 2026-02 |
| [ADR-040](ADR-040-dual-plugin-loader.md) | Dual Plugin Loader | Accepted | 2026-02 |
| [ADR-041](ADR-041-python-311-runtime.md) | Python 3.11 Runtime | Accepted | 2026-02 |
| [ADR-042](ADR-042-plugin-installer-consolidation.md) | Plugin Installer Consolidation | Accepted | 2026-02 |
| [ADR-043](ADR-043-model-lifecycle-strategy.md) | Model Lifecycle Strategy | Accepted | 2026-02-21 |
| [ADR-046](ADR-046-delete-mediator-cqrs-layer.md) | Delete Mediator/CQRS Layer (B-DELETE) | Accepted | 2026-03-03 |

## Status Legend

- **Accepted** - Decision formally documented and implemented
- **Superseded** - Replaced by another ADR

## Implementation Evidence (audit 2026-02-10)

All 32 ADRs have been formally documented with Accepted status:

- **ADR-001** (Rulebook Integration): `.cursor/rules/*.mdc`
- **ADR-003** (Agent Governance Framework): 8-role system in `docs/governance/roles/`
- **ADR-005** (Context Management): `tools/context/` module
- **ADR-007** (IPC Boundary): HTTP REST + WebSocket architecture
- **ADR-010** (Native Windows Platform): WinUI 3 application
- **ADR-015** (Architecture Integration Contract): Boundary stability contracts
- **ADR-017** (Engine Subprocess Model): `app/core/runtime/runtime_engine_enhanced.py`
- **ADR-020-022**: Debug Role, Voice Pipeline, DDD Bounded Contexts
- **ADR-023-030**: Recent architecture decisions (UI split, verification, commands, supervisor, DI migration)
- **ADR-031-032**: Gap remediation decisions (API versioning, middleware stack)
