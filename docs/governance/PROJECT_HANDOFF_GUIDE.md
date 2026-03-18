# VoiceStudio Project Handoff Guide

> **Purpose**: Single entry point for maintainers and new contributors.  
> **Owner**: Overseer (Role 0)  
> **Last Updated**: 2026-01-30  
> **Related**: [TASK-0012](../tasks/TASK-0012.md), [CANONICAL_REGISTRY.md](CANONICAL_REGISTRY.md), [STATE.md](../../.cursor/STATE.md)

---

## Executive Summary

- **Project**: VoiceStudio Quantum+ — native Windows AI voice studio (WinUI 3 + FastAPI + 44 manifest-driven engines).
- **Phase**: Post-Phase-5; all gates B–H **GREEN** (100%). Quality Ledger 33/33 DONE.
- **Key metrics**: 164+ API endpoints, 6 core panels, 44 engine manifests, Gate C UI smoke and Gate H installer lifecycle validated.
- **Source of truth**: Gate status → [Recovery Plan/QUALITY_LEDGER.md](../../Recovery%20Plan/QUALITY_LEDGER.md); session state → [.cursor/STATE.md](../../.cursor/STATE.md).

---

## Architecture Overview

- **Boundaries**: UI ↔ Backend via HTTP REST + WebSocket; Backend ↔ Engines via IPC (see [ADR-007](../architecture/decisions/ADR-007-ipc-boundary.md)).
- **Platform**: Native Windows only ([ADR-010](../architecture/decisions/ADR-010-native-windows-platform.md)); WinUI 3, not Electron.
- **Key ADRs**: [docs/architecture/decisions/README.md](../architecture/decisions/README.md) — ADR-001 through ADR-019. Index: [CANONICAL_REGISTRY.md](CANONICAL_REGISTRY.md) § Architecture.

---

## Build & Test Commands

| Action | Command |
|--------|--------|
| Build (C# WinUI) | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` |
| Test (C#) | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` |
| Test (Python) | `python -m pytest tests` |
| Gate + ledger check | `python scripts/run_verification.py` |
| Task validation | `python scripts/validator_workflow.py --task TASK-XXXX` |

Proof artifacts: `.buildlogs/verification/last_run.json`, `.buildlogs/proof_runs/`.

---

## Repository Structure

| Path | Purpose |
|------|---------|
| `src/VoiceStudio.App/` | WinUI 3 frontend (Views, ViewModels, Services) |
| `src/VoiceStudio.Core/` | Core contracts and interfaces |
| `backend/api/` | FastAPI routes and app init |
| `backend/services/` | Backend services |
| `app/core/` | Engine layer (engines, runtime) |
| `engines/` | Engine manifests (JSON) |
| `tests/` | Python pytest tests |
| `src/VoiceStudio.App.Tests/` | C# MSTest tests |
| `installer/` | Windows installer (Inno Setup) |
| `docs/governance/` | Governance, roadmap, canonical registry |
| `.cursor/STATE.md` | Current phase, active task, proof index |

---

## Key Contacts & Ownership

| Role | Responsibility | Guide |
|------|----------------|--------|
| 0 Overseer | Gate discipline, evidence, coordination | [ROLE_0_OVERSEER_GUIDE.md](roles/ROLE_0_OVERSEER_GUIDE.md) |
| 1 System Architect | Boundaries, contracts, ADRs | [ROLE_1_SYSTEM_ARCHITECT_GUIDE.md](roles/ROLE_1_SYSTEM_ARCHITECT_GUIDE.md) |
| 2 Build & Tooling | Deterministic build, CI/CD | [ROLE_2_BUILD_TOOLING_GUIDE.md](roles/ROLE_2_BUILD_TOOLING_GUIDE.md) |
| 3 UI Engineer | MVVM, VSQ tokens, WinUI 3 | [ROLE_3_UI_ENGINEER_GUIDE.md](roles/ROLE_3_UI_ENGINEER_GUIDE.md) |
| 4 Core Platform | Runtime, storage, preflight | [ROLE_4_CORE_PLATFORM_GUIDE.md](roles/ROLE_4_CORE_PLATFORM_GUIDE.md) |
| 5 Engine Engineer | Quality metrics, engine adapters | [ROLE_5_ENGINE_ENGINEER_GUIDE.md](roles/ROLE_5_ENGINE_ENGINEER_GUIDE.md) |
| 6 Release Engineer | Installer, Gate H lifecycle | [ROLE_6_RELEASE_ENGINEER_GUIDE.md](roles/ROLE_6_RELEASE_ENGINEER_GUIDE.md) |
| 7 Debug Agent | Root-cause analysis, triage | [ROLE_7_DEBUG_AGENT_GUIDE.md](roles/ROLE_7_DEBUG_AGENT_GUIDE.md) |
| Skeptical Validator | Independent verification before closure | [SKEPTICAL_VALIDATOR_GUIDE.md](SKEPTICAL_VALIDATOR_GUIDE.md) |

Role prompts: `.cursor/prompts/ROLE_PROMPTS_INDEX.md`. Escalation: [VALIDATOR_ESCALATION.md](VALIDATOR_ESCALATION.md).

---

## Active Execution (Operational Truth)

**For active execution, see [.cursor/STATE.md](../../.cursor/STATE.md) and active hardening plans.** Roadmap and spec docs below are vision/spec or historical context; STATE.md ACTIVE WINDOW is the single source of truth for current task, next steps, blockers, and proof index.

---

## Roadmap & Spec Context (Vision / Historical)

- **Roadmap**: [MASTER_ROADMAP_UNIFIED.md](MASTER_ROADMAP_UNIFIED.md) — Phases 0–5 COMPLETE; [VOICESTUDIO_COMPLETION_ROADMAP_V2.md](VOICESTUDIO_COMPLETION_ROADMAP_V2.md) for CI-enforced gaps. Vision/spec; active work in STATE.md.
- **Realignment**: Before updating plan/roadmap/roles, use [Final Sweep — Consolidated for Realignment](../reports/audit/FINAL_SWEEP_CONSOLIDATED_FOR_REALIGNMENT_2026-01-30.md) (single reference for all roles): §6 realignment checklist. Registry annotations in [CANONICAL_REGISTRY.md](CANONICAL_REGISTRY.md).
- **Governance/docs**: Registry and handoff alignment per [CANONICAL_REGISTRY.md](CANONICAL_REGISTRY.md).
- **Task briefs**: [docs/tasks/README.md](../tasks/README.md); active task and next steps in [.cursor/STATE.md](../../.cursor/STATE.md).
- **Preflight live check (Role 4)**: When backend on 8001, run `curl http://localhost:8001/api/health/preflight` and record 200 in STATE or Proof Index.

---

## Phase 6+ Track

**Phase 6+** = post-Phase-5 optional work. Two sources:

1. **EXECUTION_PLAN** ([docs/archive/legacy_worker_system/design/EXECUTION_PLAN.md](../archive/legacy_worker_system/design/EXECUTION_PLAN.md)) (archived): Phase 6 — Styles & Micro-Interactions; Phase 7 — Sanity Pass & Anti-Simplification. Prefer MASTER_ROADMAP_UNIFIED and TECH_DEBT_REGISTER for current Phase 6+.
2. **TECH_DEBT_REGISTER §4** ([TECH_DEBT_REGISTER.md](TECH_DEBT_REGISTER.md)): Optional features (streaming TTS, UI animations, more engines, MCP auto-selection, performance, cross-platform); Context Manager enhancements (OpenMemory MCP wiring, task-type classifier, issues-to-tasks); Tooling (CI/CD, skills migration, onboarding caching).

First Phase 6+ work is chosen by Overseer and scoped in a task brief (e.g. TASK-0021).

---

## Task Brief Creation

When creating a new task brief for any scope:

1. **Next ID**: Use the next sequential ID (e.g. **TASK-0022**). Confirm in `docs/tasks/` that no higher ID is already used.
2. **Template**: Copy from [docs/tasks/TASK_TEMPLATE.md](../tasks/TASK_TEMPLATE.md). Required sections: Objective, Affected Modules, Constraints, Required Proofs, Acceptance Criteria, Status.
3. **Fill scope**: Specify objective and scope; fill Affected Modules, Constraints, proofs, and acceptance criteria.
4. **Assign owner**: Set owner role (Overseer, Role 1–7) per the role system. See [ROLE_GUIDES_INDEX.md](ROLE_GUIDES_INDEX.md).
5. **Register**: Create `docs/tasks/TASK-00XX.md`; set as Active Task in [.cursor/STATE.md](../../.cursor/STATE.md) when starting work.

Lifecycle: Analyze → Blueprint → Construct → Validate. See [docs/tasks/README.md](../tasks/README.md).

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-16 | Truth-sync: Active Execution section points to STATE.md as operational truth; roadmap/spec docs marked vision/historical. Premium Reliability Coherence Pass Task 11. |
| 2026-01-30 | Created from PROJECT_HANDOFF_DOCUMENT_2025-01-28.md; updated for post-Phase-5 state; added task brief creation, Phase 6+ track, preflight check. |
| 2026-01-30 | Next Steps: added Realignment bullet linking to Final Sweep (Pre-Realignment) and CANONICAL_REGISTRY annotations for still-missing items. |
