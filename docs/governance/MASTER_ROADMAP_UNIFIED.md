> **↩ Superseded for forward-looking phase authority (2026-03-29):** Use **[VOICESTUDIO_PROFESSIONAL_ROADMAP_V3.md](VOICESTUDIO_PROFESSIONAL_ROADMAP_V3.md)** and **[PROFESSIONAL_GAP_TRACKER.md](../design/PROFESSIONAL_GAP_TRACKER.md)** for hero workflows, Phases 0–7, and execution gaps. This document remains a **historical milestone and vision** reference (Quantum+ phases); it does not override V3 sequencing.

# VoiceStudio Quantum+ Master Roadmap (Unified)

> **Version**: 1.0.0  
> **Last Updated**: 2026-02-10  
> **Owner**: Overseer (Role 0)  
> **Status**: Vision/Spec — phase and milestone reference  
> **Supersedes**: MASTER_ROADMAP.md, MASTER_ROADMAP_SUMMARY.md, MASTER_ROADMAP_INDEX.md (archived to `docs/archive/governance/`)

> **For active execution, see [.cursor/STATE.md](../../.cursor/STATE.md), **Professional Roadmap V3**, and the **Gap Tracker** (see supersede note above). Current task, next steps, and blockers live in STATE.md ACTIVE WINDOW.

This document is the **historical single source** for Quantum+ phase/milestone narrative (pre-V3). **Forward-looking phase sequencing** is defined in [VOICESTUDIO_PROFESSIONAL_ROADMAP_V3.md](VOICESTUDIO_PROFESSIONAL_ROADMAP_V3.md). Older pre-unified roadmaps remain archived under `docs/archive/governance/`.

---

## Executive Summary

**Project**: VoiceStudio Quantum+ — Native Windows AI voice studio (WinUI 3 + FastAPI + 44 manifest-driven engines)

**Current State** (2026-01-29):
- **Phase**: Post-Phase-5 (Packaging & Installer)
- **Gates**: B–H **GREEN** (100%)
- **Quality Ledger**: 33/33 **DONE**
- **Production Status**: v1.0.0 BASELINE declared **PRODUCTION-READY** for Windows 10/11
- **Active Task**: TASK-0020 (Wizard flow e2e proof)

**Key Metrics**:
- 164+ API endpoints
- 6 core panels + 12 advanced panels
- 44 engine manifests
- Gate C UI smoke: 11 nav steps, 0 binding failures
- Gate H installer lifecycle: 7/7 PASS

---

## Roadmap Phases

### Phase 0: Foundation (COMPLETE ✓)

**Status**: 100% DONE  
**Completion**: 2025-12-27

| Milestone | Status | Evidence |
|-----------|--------|----------|
| Architecture defined | ✓ | 19 ADRs, 10-part spec series |
| Engine Protocol system | ✓ | BaseEngine, manifests in `engines/` |
| XTTS, Chatterbox, Tortoise engines | ✓ | Engine adapters in `app/core/engines/` |
| Quality metrics framework | ✓ | `app/core/engines/quality_metrics.py` |
| Panel discovery system | ✓ | PanelHost, PanelStack, Registry |

---

### Phase 1: Core Backend (COMPLETE ✓)

**Status**: 100% DONE  
**Completion**: 2026-01-15

| Milestone | Status | Evidence |
|-----------|--------|----------|
| FastAPI application | ✓ | `backend/api/main.py` |
| Core endpoints (164+) | ✓ | `backend/api/routes/` |
| WebSocket support | ✓ | `backend/api/ws/realtime.py` |
| Engine router | ✓ | `backend/services/engine_router.py` |
| IBackendClient (C#) | ✓ | `src/VoiceStudio.App/Services/BackendClient.cs` |

---

### Phase 2: Audio Integration (COMPLETE ✓)

**Status**: 100% DONE  
**Completion**: 2026-01-20

| Milestone | Status | Evidence |
|-----------|--------|----------|
| Engine integration | ✓ | XTTS, Piper, Chatterbox adapters |
| Audio engine router | ✓ | Dynamic engine selection |
| Audio playback service | ✓ | NAudio/WASAPI integration |
| Audio file I/O | ✓ | Backend returns URLs, client downloads |
| Timeline audio playback | ✓ | Play/Pause/Stop/Resume in TimelineView |

---

### Phase 3: UI Core (COMPLETE ✓)

**Status**: 100% DONE  
**Completion**: 2026-01-22

| Milestone | Status | Evidence |
|-----------|--------|----------|
| 3-row shell | ✓ | MenuBar, Workspace, StatusBar |
| 4 PanelHosts | ✓ | Left, Center, Right, Bottom |
| 6 core panels | ✓ | Profiles, Timeline, EffectsMixer, Analyzer, Macro, Diagnostics |
| Design tokens | ✓ | VSQ.* tokens in DesignTokens.xaml |
| MVVM separation | ✓ | Views + ViewModels + Services |

---

### Phase 4: Quality & Testing (COMPLETE ✓)

**Status**: 100% DONE  
**Completion**: 2026-01-25

| Milestone | Status | Evidence |
|-----------|--------|----------|
| Gate D (Backend Quality) | ✓ GREEN (10/10) | Role 4 proof suite 40/40 PASS |
| Gate E (Engine Integration) | ✓ GREEN (4/4) | Baseline proof, SLO-6 met |
| Gate F (UI Compliance) | ✓ GREEN (6/6) | UI_COMPLIANCE_AUDIT, PANEL_FUNCTIONALITY_TESTS |
| Gate G (Comprehensive QA) | ✓ GREEN (3/3) | ACCESSIBILITY_TESTING_REPORT, PERFORMANCE_TESTING_REPORT, SECURITY_AUDIT_REPORT |
| Quality Ledger | ✓ 33/33 DONE | docs/archive/Recovery_Plan/QUALITY_LEDGER.md |

---

### Phase 5: Packaging & Installer (COMPLETE ✓)

**Status**: 100% DONE  
**Completion**: 2026-01-29

| Milestone | Status | Evidence |
|-----------|--------|----------|
| Gate H (Packaging & Installer) | ✓ GREEN (1/1) | Installer lifecycle 7/7 PASS |
| Gate C (Release Build) | ✓ GREEN | 21 UI smoke runs, 0 binding failures |
| Production readiness | ✓ DECLARED | PRODUCTION_READINESS.md |
| Phase 5 closure | ✓ COMPLETE | PHASE_5_CLOSURE_REPORT_2026-01-29.md |

---

## Phase 6+: Optional Work & Tech Debt

**Status**: ACTIVE — Optional features and tech debt remediation  
**Entry Criteria**: All gates B-H GREEN ✓

### Phase 6: Optional Enhancements

| Feature | Description | Effort | Priority | Owner |
|---------|-------------|--------|----------|-------|
| Advanced panel backend integration | **COMPLETE.** All 9 innovative panels (TextSpeechEditor, Prosody, SpatialAudio, AIMixingMastering, EffectsMixer, etc.) have comprehensive backend integration. 210 methods in BackendClient, 184 in IBackendClient. | 36-72h | Medium | Role 3 |
| UI automation framework | **COMPLETE.** FlaUI smoke tests (5 journeys), Python page objects (Settings, ThemeEditor, ProfileEditor), UI_TEST_MATRIX.md documentation. | 16-24h | Medium | Role 3 |
| Wizard e2e automation | Execute wizard_flow_proof.py with ≥3s speech reference | 1-2h | Medium | Role 3/5 |
| OpenMemory MCP wiring | **COMPLETE.** `_try_openmemory_mcp_protocol()` is fully implemented in memory_adapter.py. MCP enabled by default in context-sources.json. Setup scripts and tests added. | 4-8h | Medium | Role 4 |
| Theme Editor | **COMPLETE.** Accent persistence, ColorPicker for custom colors, ThemeEditorViewModel MVVM, Theme.HighContrast.xaml and Theme.Default.xaml added. Unit tests included. | 8-16h | Low | Role 3 |
| Plugin Gallery UI | **COMPLETE.** IPluginGateway interface, PluginGateway implementation, PluginGalleryViewModel with search/filter/install, PluginCard control, PluginGalleryView and PluginDetailView panels, MockPluginGateway for tests, comprehensive unit tests. Registered in panel system. | 24-32h | Medium | Role 3 |
| Design system expansion | Visualization/animation tokens | 8-16h | Low | Role 3 |
| Accessibility enhancements | Screen reader, ARIA, focus | 8-16h | Medium | Role 3 |
| UI performance optimization | Virtualization, caching, profiling | 8-16h | Low | Role 3/4 |
| UX refinements | Animations, tooltips | 8-16h | Low | Role 3 |

### Phase 7: Quality Infrastructure (NEW)

**Status**: COMPLETE (2026-02-11)  
**Entry Criteria**: All gates B-H GREEN ✓ (satisfied)  
**Goal**: Encode lessons from Error Pattern Retrospective into architecture

| Work Stream | Description | Effort | Owner | Status |
|-------------|-------------|--------|-------|--------|
| WS-1 | Error Visibility Layer (ErrorBoundary.cs, error_boundary.py) | 16-24h | Role 4 | Complete |
| WS-2 | Python Path Standardization (_env_setup.py) | 8-16h | Role 4 | Complete |
| WS-3 | Empty Catch Block Remediation (infrastructure) | 24-32h | Role 7 | Complete |
| WS-4 | XAML Safety Protocol (lint_xaml.py, xaml_bisect.py) | 16-24h | Role 2 | Complete |
| WS-5 | Configuration Matrix Testing (Debug+Release CI) | 8-16h | Role 2/5 | Complete |
| WS-6 | Observability Infrastructure (correlation IDs) | 24-32h | Role 4 | Complete |
| WS-7 | Contract Validation (OpenAPI, engine manifests) | 16-24h | Role 1/5 | Complete |
| WS-8 | Verification Automation (run_verification.py, quick_verify.ps1) | 8-16h | Role 2 | Complete |

**Total Effort**: 120-184 hours (15-23 person-days)

**References**:
- [Error Pattern Retrospective](../reports/post_mortem/ERROR_PATTERN_RETROSPECTIVE_2026-02-04.md)
- [XAML Change Protocol](../developer/XAML_CHANGE_PROTOCOL.md)

---

### Phase 8: Tech Debt Remediation (formerly Phase 7)

Per [TECH_DEBT_REGISTER.md](TECH_DEBT_REGISTER.md):

| TD ID | Description | Priority | Owner | Status |
|-------|-------------|----------|-------|--------|
| ~~TD-001~~ | ~~Chatterbox torch>=2.6 / per-engine venv~~ | HIGH | Role 5 | **RESOLVED** (2026-02-10) |
| ~~TD-002~~ | ~~Gate C Release full fix (revert NoWarn)~~ | HIGH | Role 2 | **RESOLVED** (2026-02-10) |
| ~~TD-003~~ | ~~protobuf CVE-2026-0994~~ | MEDIUM | Role 4/5 | **RESOLVED** (2026-02-10) |
| ~~TD-004~~ | ~~ViewModel DI migration~~ | MEDIUM | Role 3/4 | **RESOLVED** (2026-02-10) |
| ~~TD-005~~ | ~~Wizard flow e2e automation~~ | MEDIUM | Role 3/5 | **RESOLVED** (2026-02-02) |
| ~~TD-007~~ | ~~Warning count reduction~~ | LOW | Role 2/3 | **RESOLVED** (2026-02-10) |

### Phase 9: Architecture Gap Remediation (formerly Phase 8)

Per [ARCHITECTURE_CROSS_REFERENCE_2026-01-30.md](../reports/verification/ARCHITECTURE_CROSS_REFERENCE_2026-01-30.md):

| TD ID | Description | Spec Source | Priority | Status |
|-------|-------------|-------------|----------|--------|
| ~~TD-013~~ | ~~VRAM Resource Scheduler~~ | ChatGPT Part 7 | HIGH | **RESOLVED** (2026-02-10) |
| ~~TD-014~~ | ~~Circuit Breaker pattern~~ | ChatGPT Part 3 | MEDIUM | **RESOLVED** (2026-02-10) |
| ~~TD-015~~ | ~~Venv Families (8 families)~~ | ChatGPT Part 4 | MEDIUM | **RESOLVED** (2026-02-10) |
| ~~TD-016~~ | ~~Engine Manifest Schema v2~~ | ChatGPT Part 4 | LOW | **RESOLVED** (2026-02-10) |

### Phase 10: Documentation Completeness (formerly Phase 9)

**Status**: COMPLETE (2026-02-11)  
**Entry Criteria**: All gates B-H GREEN ✓ (satisfied)

Per [FINAL_SWEEP_MISSING_FILES_GAPS_2026-01-29.md](../reports/verification/FINAL_SWEEP_MISSING_FILES_GAPS_2026-01-29.md):

| Item | Description | Effort | Owner | Status |
|------|-------------|--------|-------|--------|
| ADR Index complete | All 30 ADRs indexed in decisions/README.md | 1h | Role 1 | ✓ DONE |
| ADR-020 Debug Role | Debug Role Architecture ADR | 0.5h | Role 1 | ✓ DONE |
| ADR-021 Voice Pipeline | Voice AI Pipeline Architecture ADR | 0.5h | Role 1 | ✓ DONE |
| ADR-022 DDD Contexts | DDD Bounded Contexts ADR | 0.5h | Role 1 | ✓ DONE |
| Broken ADR references | Fix references in 5 governance/developer docs | 0.5h | Role 1 | ✓ DONE |
| Architecture README | Updated docs/architecture/README.md | 0.5h | Role 1 | ✓ DONE |
| Missing task briefs | Backfill TASK-0009, 0011-0019, 0023, 0026 | 8-11h | Role 0 | ✓ DONE |
| Remaining placeholders | ADR-002, 004-014, 016 | 4-6h | Role 1 | ✓ DONE |

---

## Current Work (2026-01-29)

### Active Tasks

| Task ID | Title | Owner | Status | Next Action |
|---------|-------|-------|--------|-------------|
| **TASK-0020** | Wizard Flow E2E Proof (TD-005) | Role 3/5 | Complete | Proof runs completed |
| **TASK-0021** | OpenMemory MCP Wiring | Role 4/1 | Complete (2026-02-11) | MCP enabled, tests added |
| **TASK-0022** | Git History Reconstruction | Role 0 | Complete | 80+ files recovered |
| **TASK-0023** | Interface Implementations + Pre-commit Hooks | Role 3/4 | Complete (2026-02-11) | All interfaces implemented, hooks configured |

### Next 3 Steps

1. **TASK-0020**: Execute wizard flow e2e proof when backend on 8001 with ≥3s speech reference
2. **Tooling refresh**: Run `python scripts/run_verification.py` when starting next workstream
3. **Role handoff**: Use `/role-ui-engineer` or `/role-engine-engineer` for TASK-0020; `/role-core-platform` or `/role-system-architect` for TASK-0021

---

## Future Phases (Phase 11+)

### Advanced Features (Phase 6+ Optional)

From [TECH_DEBT_REGISTER §4](TECH_DEBT_REGISTER.md):

- Real-time streaming voice synthesis
- Advanced UI animations (Fluent, micro-interactions)
- Additional engine families (Bark, Tacotron, RVC v2)
- MCP auto-selection (task-type → MCP)
- Performance optimizations (UI <100ms, engine throughput)
- Cross-platform (macOS, Linux via Avalonia/MAUI)
- Task-type classifier
- Issues-to-tasks automation
- CI/CD pipeline
- Agent Skills migration
- Onboarding packet caching

---

## Governance & Process

### Gate System

All gates B-H are **GREEN** (100%):

| Gate | Name | Status | Evidence |
|------|------|--------|----------|
| B | Baseline Build | GREEN | `dotnet build` 0 errors |
| C | Release Build & UI Smoke | GREEN | 21 UI smoke runs, 0 binding failures |
| D | Backend Quality | GREEN (10/10) | Role 4 proof suite 40/40 PASS |
| E | Engine Integration | GREEN (4/4) | Baseline proof, SLO-6 met |
| F | UI Compliance | GREEN (6/6) | UI_COMPLIANCE_AUDIT |
| G | Comprehensive QA | GREEN (3/3) | Accessibility, Performance, Security |
| H | Packaging & Installer | GREEN (1/1) | Installer lifecycle 7/7 PASS |

**Source**: [docs/archive/Recovery_Plan/QUALITY_LEDGER.md](../archive/Recovery_Plan/QUALITY_LEDGER.md)

### Role System

8 roles + Skeptical Validator:

| Role | Responsibility | Guide | Prompt |
|------|----------------|-------|--------|
| 0 Overseer | Gate discipline, evidence, coordination | [ROLE_0_OVERSEER_GUIDE.md](roles/ROLE_0_OVERSEER_GUIDE.md) | [ROLE_0_OVERSEER_PROMPT.md](../../.cursor/prompts/ROLE_0_OVERSEER_PROMPT.md) |
| 1 System Architect | Boundaries, contracts, ADRs | [ROLE_1_SYSTEM_ARCHITECT_GUIDE.md](roles/ROLE_1_SYSTEM_ARCHITECT_GUIDE.md) | [ROLE_1_SYSTEM_ARCHITECT_PROMPT.md](../../.cursor/prompts/ROLE_1_SYSTEM_ARCHITECT_PROMPT.md) |
| 2 Build & Tooling | Deterministic build, CI/CD | [ROLE_2_BUILD_TOOLING_GUIDE.md](roles/ROLE_2_BUILD_TOOLING_GUIDE.md) | [ROLE_2_BUILD_TOOLING_PROMPT.md](../../.cursor/prompts/ROLE_2_BUILD_TOOLING_PROMPT.md) |
| 3 UI Engineer | MVVM, VSQ tokens, WinUI 3 | [ROLE_3_UI_ENGINEER_GUIDE.md](roles/ROLE_3_UI_ENGINEER_GUIDE.md) | [ROLE_3_UI_ENGINEER_PROMPT.md](../../.cursor/prompts/ROLE_3_UI_ENGINEER_PROMPT.md) |
| 4 Core Platform | Runtime, storage, preflight | [ROLE_4_CORE_PLATFORM_GUIDE.md](roles/ROLE_4_CORE_PLATFORM_GUIDE.md) | [ROLE_4_CORE_PLATFORM_PROMPT.md](../../.cursor/prompts/ROLE_4_CORE_PLATFORM_PROMPT.md) |
| 5 Engine Engineer | Quality metrics, engine adapters | [ROLE_5_ENGINE_ENGINEER_GUIDE.md](roles/ROLE_5_ENGINE_ENGINEER_GUIDE.md) | [ROLE_5_ENGINE_ENGINEER_PROMPT.md](../../.cursor/prompts/ROLE_5_ENGINE_ENGINEER_PROMPT.md) |
| 6 Release Engineer | Installer, Gate H lifecycle | [ROLE_6_RELEASE_ENGINEER_GUIDE.md](roles/ROLE_6_RELEASE_ENGINEER_GUIDE.md) | [ROLE_6_RELEASE_ENGINEER_PROMPT.md](../../.cursor/prompts/ROLE_6_RELEASE_ENGINEER_PROMPT.md) |
| 7 Debug Agent | Issue triage, escalation, root-cause | [ROLE_7_DEBUG_AGENT_GUIDE.md](roles/ROLE_7_DEBUG_AGENT_GUIDE.md) | [ROLE_7_DEBUG_AGENT_PROMPT.md](../../.cursor/prompts/ROLE_7_DEBUG_AGENT_PROMPT.md) |
| Skeptical Validator | Independent verification before closure | [SKEPTICAL_VALIDATOR_GUIDE.md](SKEPTICAL_VALIDATOR_GUIDE.md) | [SKEPTICAL_VALIDATOR_PROMPT.md](../../.cursor/prompts/SKEPTICAL_VALIDATOR_PROMPT.md) |

---

## Technical Debt

See [TECH_DEBT_REGISTER.md](TECH_DEBT_REGISTER.md) for complete list. Summary:

### High Priority
- TD-001: Chatterbox torch>=2.6 / per-engine venv
- TD-002: Gate C Release full fix (revert NoWarn suppressions)

### Medium Priority
- TD-003: protobuf CVE-2026-0994
- TD-004: ViewModel DI migration
- TD-005: Wizard flow e2e automation (TASK-0020)

### Low Priority
- TD-007: Warning count reduction (~4990)

### Architecture Gaps (from ChatGPT Spec)
- TD-013: VRAM Resource Scheduler
- TD-014: Circuit Breaker pattern
- TD-015: Venv Families (12 families)
- TD-016: Engine Manifest Schema v2

---

## References

- **Gate Status**: [docs/archive/Recovery_Plan/QUALITY_LEDGER.md](../archive/Recovery_Plan/QUALITY_LEDGER.md)
- **Session State**: [.cursor/STATE.md](../../.cursor/STATE.md)
- **Production Readiness**: [PRODUCTION_READINESS.md](../PRODUCTION_READINESS.md)
- **Tech Debt**: [TECH_DEBT_REGISTER.md](TECH_DEBT_REGISTER.md)
- **Optional Tasks**: [OPTIONAL_TASK_INVENTORY.md](OPTIONAL_TASK_INVENTORY.md)
- **Project Handoff**: [PROJECT_HANDOFF_GUIDE.md](PROJECT_HANDOFF_GUIDE.md)

---

## Changelog

| Date | Change | Author |
|------|--------|--------|
| 2026-01-29 | Initial creation — consolidates roadmap state post-Phase-5 | Overseer (Role 0) |
| 2026-02-04 | Added Phase 7: Quality Infrastructure; renumbered subsequent phases | Phase 7 Plan Implementation |
| 2026-02-10 | Phase 10 in progress: ADR-020/021/022 created, ADR index complete, architecture README updated | Phase 10 Implementation |
