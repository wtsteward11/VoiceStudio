# Role Guides Index

> **Owner**: Overseer (Role 0)  
> **Last Updated**: 2026-03-28  
> **Purpose**: Master index for all role guides with phase-gate-role matrix and quick reference.

---

## Role System Overview

VoiceStudio uses a **9-role system** (Roles 0–8) + **Skeptical Validator** (cross-cutting subagent) for governance, development, and quality assurance. Each role has:
- **Guide**: Detailed responsibilities, boundaries, workflows (in `docs/governance/roles/`)
- **Prompt**: Complete system prompt for AI agent invocation (in `.cursor/prompts/`)
- **Ownership**: Specific modules, gates, and deliverables

---

## All Roles

| # | Role | Guide | Prompt | Key Responsibility |
|---|------|-------|--------|-------------------|
| **0** | **Overseer** | [ROLE_0_OVERSEER_GUIDE.md](roles/ROLE_0_OVERSEER_GUIDE.md) | [ROLE_0_OVERSEER_PROMPT.md](../../.cursor/prompts/ROLE_0_OVERSEER_PROMPT.md) | Gate enforcement, evidence, coordination, task assignment |
| **1** | **System Architect** | [ROLE_1_SYSTEM_ARCHITECT_GUIDE.md](roles/ROLE_1_SYSTEM_ARCHITECT_GUIDE.md) | [ROLE_1_SYSTEM_ARCHITECT_PROMPT.md](../../.cursor/prompts/ROLE_1_SYSTEM_ARCHITECT_PROMPT.md) | Boundaries, contracts, ADRs, architecture decisions |
| **2** | **Build & Tooling** | [ROLE_2_BUILD_TOOLING_GUIDE.md](roles/ROLE_2_BUILD_TOOLING_GUIDE.md) | [ROLE_2_BUILD_TOOLING_PROMPT.md](../../.cursor/prompts/ROLE_2_BUILD_TOOLING_PROMPT.md) | Deterministic builds, CI/CD, compiler, warnings |
| **3** | **UI Engineer** | [ROLE_3_UI_ENGINEER_GUIDE.md](roles/ROLE_3_UI_ENGINEER_GUIDE.md) | [ROLE_3_UI_ENGINEER_PROMPT.md](../../.cursor/prompts/ROLE_3_UI_ENGINEER_PROMPT.md) | MVVM, VSQ tokens, WinUI 3, panels, binding failures |
| **4** | **Core Platform** | [ROLE_4_CORE_PLATFORM_GUIDE.md](roles/ROLE_4_CORE_PLATFORM_GUIDE.md) | [ROLE_4_CORE_PLATFORM_PROMPT.md](../../.cursor/prompts/ROLE_4_CORE_PLATFORM_PROMPT.md) | Runtime, storage, preflight, backend services, Context Manager |
| **5** | **Engine Engineer** | [ROLE_5_ENGINE_ENGINEER_GUIDE.md](roles/ROLE_5_ENGINE_ENGINEER_GUIDE.md) | [ROLE_5_ENGINE_ENGINEER_PROMPT.md](../../.cursor/prompts/ROLE_5_ENGINE_ENGINEER_PROMPT.md) | Quality metrics, engine adapters, baseline proofs, SLO-6 |
| **6** | **Release Engineer** | [ROLE_6_RELEASE_ENGINEER_GUIDE.md](roles/ROLE_6_RELEASE_ENGINEER_GUIDE.md) | [ROLE_6_RELEASE_ENGINEER_PROMPT.md](../../.cursor/prompts/ROLE_6_RELEASE_ENGINEER_PROMPT.md) | Installer, Gate H lifecycle, packaging, distribution |
| **7** | **Debug Agent** | [ROLE_7_DEBUG_AGENT_GUIDE.md](roles/ROLE_7_DEBUG_AGENT_GUIDE.md) | [ROLE_7_DEBUG_AGENT_PROMPT.md](../../.cursor/prompts/ROLE_7_DEBUG_AGENT_PROMPT.md) | Root-cause analysis, issue triage, system-wide fixes, validation |
| **8** | **Project Intelligence Analyst** | [ROLE_8_PROJECT_INTELLIGENCE_ANALYST_GUIDE.md](roles/ROLE_8_PROJECT_INTELLIGENCE_ANALYST_GUIDE.md) | [ROLE_8_PROJECT_INTELLIGENCE_ANALYST_PROMPT.md](../../.cursor/prompts/ROLE_8_PROJECT_INTELLIGENCE_ANALYST_PROMPT.md) | Read-only intelligence: maps, traces, contradiction memos, external research — **no repo mutations** |
| **—** | **Skeptical Validator** | [SKEPTICAL_VALIDATOR_GUIDE.md](SKEPTICAL_VALIDATOR_GUIDE.md) | [SKEPTICAL_VALIDATOR_PROMPT.md](../../.cursor/prompts/SKEPTICAL_VALIDATOR_PROMPT.md) | Cross-cutting validation before task closure; read-only, fast model |

---

## Phase-Gate-Role Matrix

| Phase | Gates | Primary Roles | Supporting Roles | Deliverables |
|-------|-------|---------------|------------------|--------------|
| **Phase 0: Foundation** | — | Role 1 (Architect), Role 5 (Engine) | Role 2 (Build), Role 4 (Platform) | ADRs, engine protocol, quality metrics framework |
| **Phase 1: Core Backend** | **Gate B** (Build) | Role 4 (Platform), Role 5 (Engine) | Role 2 (Build) | FastAPI app, 164+ endpoints, engine router, IBackendClient |
| **Phase 2: Audio Integration** | **Gate E** (Engine Integration) | Role 5 (Engine), Role 4 (Platform) | Role 3 (UI) | Engine adapters, audio playback, baseline proof |
| **Phase 3: UI Core** | **Gate F** (UI Compliance) | Role 3 (UI) | Role 2 (Build), Role 4 (Platform) | 3-row shell, 4 PanelHosts, 6 core panels, VSQ tokens |
| **Phase 4: Quality & Testing** | **Gate D** (Backend Quality), **Gate G** (Comprehensive QA) | Role 4 (Platform), Role 5 (Engine), Role 3 (UI) | Role 0 (Overseer), Validator | Role 4 proof suite, accessibility, performance, security |
| **Phase 5: Packaging & Installer** | **Gate C** (Release Build), **Gate H** (Packaging & Installer) | Role 6 (Release), Role 2 (Build) | Role 3 (UI), Role 0 (Overseer) | UI smoke, installer lifecycle, production readiness |
| **Phase 6+: Optional & Tech Debt** | — | All roles per task assignment | Role 0 (Overseer) coordinates | Tech debt remediation, optional features |

---

## Role Ownership by Module

| Module | Primary Owner | Supporting |
|--------|---------------|------------|
| `src/VoiceStudio.App/` (Views, ViewModels) | Role 3 (UI) | Role 2 (Build) |
| `src/VoiceStudio.Core/` (Contracts) | Role 1 (Architect) | Role 4 (Platform) |
| `backend/api/` (Routes, main.py) | Role 4 (Platform) | Role 5 (Engine) |
| `backend/services/` | Role 4 (Platform) | Role 5 (Engine) |
| `app/core/engines/` | Role 5 (Engine) | Role 4 (Platform) |
| `engines/*.json` (Manifests) | Role 5 (Engine) | Role 1 (Architect) |
| `tests/` (Python) | Role 4 (Platform), Role 5 (Engine) | Role 0 (Overseer) |
| `src/VoiceStudio.App.Tests/` (C#) | Role 3 (UI), Role 2 (Build) | Role 0 (Overseer) |
| `installer/` | Role 6 (Release) | Role 2 (Build) |
| `tools/overseer/` | Role 0 (Overseer) | Role 1 (Architect) |
| `.cursor/` (Rules, prompts, STATE) | Role 0 (Overseer) | Role 1 (Architect) |
| `docs/` (Governance, architecture) | Role 0 (Overseer), Role 1 (Architect) | All roles |
| **All modules (read-only)** | — | Role 8 (Project Intelligence Analyst) — evidence-only analysis; no primary ownership |

---

## Role Invocation

Use role-specific prompts to invoke a role for a task:

```bash
# Example: Invoke UI Engineer for TASK-0020
/role-ui-engineer
"Execute TASK-0020: Wizard flow e2e proof. See docs/tasks/TASK-0020.md."
```

Or use `.cursor/commands/` shortcuts (if available).

---

## Escalation and Handoffs

- **Cross-role escalation**: See [CROSS_ROLE_ESCALATION_MATRIX.md](CROSS_ROLE_ESCALATION_MATRIX.md) and [HANDOFF_PROTOCOL.md](HANDOFF_PROTOCOL.md).
- **Validator escalation**: See [VALIDATOR_ESCALATION.md](VALIDATOR_ESCALATION.md).
- **Issue-to-task**: Debug Agent (Role 7) can create task briefs from critical issues; see [DEBUG_ROLE_INTEGRATION_GUIDE.md](../developer/DEBUG_ROLE_INTEGRATION_GUIDE.md).

---

## Quick Reference

| Need | Role | Command |
|------|------|---------|
| Gate status check | Overseer | `python -m tools.overseer.cli.main gate status` |
| Ledger validation | Overseer | `python -m tools.overseer.cli.main ledger validate` |
| Build fix | Build & Tooling | `/role-build-tooling` |
| UI binding failure | UI Engineer | `/role-ui-engineer` |
| Backend route issue | Core Platform | `/role-core-platform` |
| Engine proof | Engine Engineer | `/role-engine-engineer` |
| Installer issue | Release Engineer | `/role-release-engineer` |
| Root-cause analysis | Debug Agent | `/role-debug-agent` |
| Deep dive / trace / research (read-only) | Project Intelligence Analyst | `/role-project-intelligence-analyst` |
| ADR needed | System Architect | `/role-system-architect` |
| Task closure validation | Skeptical Validator | `python scripts/validator_workflow.py --task TASK-NNNN` |

---

## References

- [ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md](../../Recovery%20Plan/ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md) — Role playbooks and handshake rules
- [ROLE_CHEATSHEET.md](../developer/ROLE_CHEATSHEET.md) — Quick one-liner prompts
- [CANONICAL_REGISTRY.md](CANONICAL_REGISTRY.md) — Document registry
- [.cursor/STATE.md](../../.cursor/STATE.md) — Current phase, active task
