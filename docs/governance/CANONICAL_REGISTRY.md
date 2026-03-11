# Canonical Document Registry

This registry is the single source of truth for all canonical documents in VoiceStudio.
Before creating a new document, check this registry to ensure the topic isn't already covered.

> **Last Updated**: 2026-03-05 (Feature Catalog Master adoption)

---

## Rules and Governance

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Agent Rules | `.cursor/rules/*.mdc` | 2026-02-19 | 42 files across 8 categories |
| Error Resolution Standard | `.cursor/rules/workflows/error-resolution.mdc` | 2026-02-19 | Mandatory error discovery, logging, and professional resolution standards |
| No Deferral on Encounter | `.cursor/rules/quality/no-deferral-on-encounter.mdc` | 2026-02-19 | Pre-existing issues MUST be fixed when encountered — no kicking the can |
| Human Rules Reference (Legacy) | `docs/archive/legacy_worker_system/governance/MASTER_RULES_COMPLETE.md` | 2026-01-30 | **ARCHIVED** — Legacy worker/overseer rules; use `.cursor/rules/*.mdc` and [ROLE_GUIDES_INDEX](ROLE_GUIDES_INDEX.md) for current governance |
| Rule Proposal Template | `docs/governance/templates/RULE_PROPOSAL_TEMPLATE.md` | — | **DEFERRED** — Template not yet created; low priority |
| Rule Review Checklist | `.cursor/rules/quality/rule-review.mdc` | 2026-01-25 | Quality checklist for rule review |
| Document Governance | `docs/governance/DOCUMENT_GOVERNANCE.md` | 2026-01-30 | File creation and lifecycle; 4-gate check, versioning, archive workflow |
| Archive Policy | `docs/governance/ARCHIVE_POLICY.md` | — | **DEFERRED** — Use [document-lifecycle.mdc](.cursor/rules/workflows/document-lifecycle.mdc) and archive structure in `docs/archive/` |
| Governance Lock | `docs/governance/GOVERNANCE_LOCK.md` | — | **DEFERRED** — Low priority |
| Definition of Done | `docs/governance/DEFINITION_OF_DONE.md` | 2026-01-25 | Consolidated completion criteria |
| Session State | `.cursor/STATE.md` | 2026-01-25 | Active task, phase, proofs |
| Memory Index | `openmemory.md` | 2026-01-25 | Living project index for AI context |
| **Project Handoff Guide** | `docs/governance/PROJECT_HANDOFF_GUIDE.md` | 2026-01-30 | Maintainer entry point; gate status, build/test, structure, roles, task brief creation |
| **Tech Debt Register** | `docs/governance/TECH_DEBT_REGISTER.md` | 2026-01-29 | Consolidated technical debt, limitations, and future enhancements; categorized by priority (High/Medium/Low) |
| **Production Readiness Statement** | `docs/PRODUCTION_READINESS.md` | 2026-01-30 | Formal production readiness declaration for v1.0.0 BASELINE; capabilities, limitations, quality gates, support |
| Task Brief System | `docs/tasks/README.md` | 2026-01-30 | Task brief workflow and conventions; lifecycle: Analyze → Blueprint → Construct → Validate |
| Task Brief Template | `docs/tasks/TASK_TEMPLATE.md` | 2026-01-30 | Standard task brief template; new briefs: use next ID (e.g. TASK-0023) per [PROJECT_HANDOFF_GUIDE.md](PROJECT_HANDOFF_GUIDE.md) § Task brief creation |
| Prompt Library | `.cursor/commands/` | 2026-01-25 | Reusable AI prompts and roles |
| Completion Evidence Guard | `tools/overseer/verification/completion_guard.py` | 2026-02-01 | Prevents completion markers in uncommitted changes; integrated with verification and stop hook |
| **Compatibility Matrix** | `config/compatibility_matrix.yml` | 2026-02-02 | Centralized version pins, dependency constraints, protected surfaces; validated by `scripts/check_compatibility_matrix.py` |
| **CODEOWNERS** | `.github/CODEOWNERS` | 2026-02-02 | Protected surface ownership mapping for PR review auto-assignment |
| **AI Agent Safety Rule** | `.cursor/rules/workflows/auto-mode-safety.mdc` | 2026-02-02 | Mandatory scaffolding, matrix checks, protected surface handling for AI agents |
| **Branch Merge Policy** | `docs/governance/BRANCH_MERGE_POLICY.md` | 2026-02-02 | Divergence limits, branch lifecycle, merge strategies; closes TD-010 |
| **Change Control Rules** | `docs/governance/CHANGE_CONTROL_RULES.md` | 2026-02-09 | Non-negotiable verification gate, stabilization protocol, Cursor agent operating protocol, blast radius limits |
| **Verification Harness Rule** | `.cursor/rules/workflows/verification-harness.mdc` | 2026-02-09 | Agent rule for verify.ps1 usage; "no green = no merge" enforcement |
| **Plugin System Guidelines** | `docs/governance/PLUGIN_SYSTEM_GUIDELINES.md` | 2026-02-16 | Canonical plugin governance: architecture, security, performance, DX, testing, UI, compatibility, risk, observability (10 sections). Companion to ADR-036. |
| **Provenance Policy** | `docs/governance/PROVENANCE_POLICY.md` | 2026-03-01 | Best-effort provenance and usage recording for audio outputs; do not claim full traceability |
| **Completion Roadmap v2.0** | `docs/governance/VOICESTUDIO_COMPLETION_ROADMAP_V2.md` | 2026-03-03 | CI-enforced hardened roadmap for v1.1.0; 7 gaps, 6 phases (0/A/B/C/D/E/F), 5 permanent CI invariants |
| **Feature Catalog Master** | `docs/governance/FEATURE_CATALOG_MASTER.md` | 2026-03-05 | Single canonical feature inventory; 47 panels, API/engine/plugin surface; machine appendix: [FEATURE_CATALOG_MASTER.appendix.json](FEATURE_CATALOG_MASTER.appendix.json); CI drift check in `tests/ci/test_feature_catalog_appendix.py` |
| **Finish Line: Personal Studio** | `docs/governance/FINISH_LINE_PERSONAL_STUDIO.md` | 2026-03-06 | Acceptance criteria for workspaces CRUD/import/export, tool catalog, docking/resize/collapse persistence, restore failure recovery; manual thrash test; build determinism rule |

## Architecture

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Architecture (Comprehensive)** | `docs/developer/ARCHITECTURE.md` | 2026-02-04 | **CANONICAL** — Complete architecture reference (2400+ lines); supersedes all docs/design/architecture*.md files (archived to `docs/archive/architecture_consolidated/`) |
| Architecture Index | `docs/architecture/README.md` | 2026-01-25 | Entry point; architecture content lives in README + ADRs. |
| System Architecture (Part series) | `docs/architecture/Part*.md` | — | **DEFERRED** — 10-part series from ChatGPT spec; use `docs/architecture/README.md` + ADRs as canonical architecture source |
| Decisions (ADRs) | `docs/architecture/decisions/ADR-*.md` | 2026-01-25 | Architecture Decision Records |
| ADR Index | `docs/architecture/decisions/README.md` | 2026-01-25 | ADR listing and template |
| Rulebook Integration ADR | `docs/architecture/decisions/ADR-001-rulebook-integration.md` | 2026-01-25 | Rulebook and rule governance |
| Document Governance ADR | `docs/architecture/decisions/ADR-002-document-governance.md` | 2026-01-25 | Document governance and lifecycle |
| Agent Governance Framework ADR | `docs/architecture/decisions/ADR-003-agent-governance-framework.md` | 2026-01-25 | Agent governance and roles |
| MessagePack IPC ADR | `docs/architecture/decisions/ADR-004-messagepack-ipc.md` | 2026-01-25 | MessagePack for IPC serialization |
| Context Management ADR | `docs/architecture/decisions/ADR-005-context-management.md` | 2026-01-25 | Context management system |
| Cursor Rules ADR | `docs/architecture/decisions/ADR-006-cursor-rules-system.md` | 2026-01-25 | Enhanced Cursor rules system |
| IPC Boundary ADR | `docs/architecture/decisions/ADR-007-ipc-boundary.md` | 2026-01-25 | UI-Backend IPC boundary definition |
| Architecture Patterns ADR | `docs/architecture/decisions/ADR-008-architecture-patterns.md` | 2026-01-25 | Core architecture patterns |
| AI-Native Development ADR | `docs/architecture/decisions/ADR-009-ai-native-development.md` | 2026-01-25 | AI-native development patterns |
| Native Windows Platform ADR | `docs/architecture/decisions/ADR-010-native-windows-platform.md` | 2026-01-25 | WinUI 3 native platform choice |
| Context Manager Architecture ADR | `docs/architecture/decisions/ADR-011-context-manager-architecture.md` | 2026-01-25 | Context manager architecture |
| Roadmap Integration ADR | `docs/architecture/decisions/ADR-012-roadmap-integration.md` | 2026-01-25 | Roadmap integration scaffolding |
| OpenTelemetry Tracing ADR | `docs/architecture/decisions/ADR-013-opentelemetry-tracing.md` | 2026-01-25 | OpenTelemetry distributed tracing |
| Agent Skills ADR | `docs/architecture/decisions/ADR-014-agent-skills.md` | 2026-01-25 | Agent skills integration |
| Architecture Integration Contract ADR | `docs/architecture/decisions/ADR-015-architecture-integration-contract.md` | 2026-01-25 | Integration contract |
| **Gate C Artifact Choice ADR** | `docs/architecture/decisions/ADR-016-gate-c-artifact-choice.md` | 2026-01-29 | Unpackaged self-contained apphost EXE as Gate C launch artifact |
| Engine Subprocess Model ADR | `docs/architecture/decisions/ADR-017-engine-subprocess-model.md` | 2026-01-25 | Engine subprocess isolation model |
| Named Pipes to HTTP ADR | `docs/architecture/decisions/ADR-018-named-pipes-http.md` | 2026-01-25 | Named pipes replaced with HTTP |
| Orchestration in Python ADR | `docs/architecture/decisions/ADR-019-orchestration-in-python.md` | 2026-01-25 | C# orchestration moved to Python |
| UI Assembly Split ADR | `docs/architecture/decisions/ADR-023-ui-assembly-split.md` | 2026-01-30 | UI assembly modularization |
| Completion Evidence Guard ADR | `docs/architecture/decisions/ADR-024-completion-evidence-guard.md` | 2026-02-01 | Enforce completion markers committed before verification passes |
| **Compatibility Matrix ADR** | `docs/architecture/decisions/ADR-025-compatibility-matrix-and-scaffolding.md` | 2026-02-02 | Centralized version pins, scaffolding tools, CODEOWNERS, AI agent safety |
| **Infrastructure Remediation ADR** | `docs/architecture/decisions/ADR-026-infrastructure-remediation.md` | 2026-02-02 | Activation of dormant development infrastructure (telemetry, issues, context) |
| **Unified Verification Harness ADR** | `docs/architecture/decisions/ADR-027-unified-verification-harness.md` | 2026-02-09 | Single command verification, 8 stages, fail-fast, "no green = no merge" rule |
| **Unified Command Architecture ADR** | `docs/architecture/decisions/ADR-028-unified-command-architecture.md` | 2026-02-08 | Hybrid command system: Registry for global/routed, ViewModel for panel-local |
| Hybrid Supervisor ADR | `docs/architecture/decisions/ADR-029-hybrid-supervisor.md` | 2026-02-09 | Hybrid supervisor architecture |
| **ViewModel DI Migration ADR** | `docs/architecture/decisions/ADR-030-viewmodel-di-migration.md` | 2026-02-09 | ViewModel constructor DI migration from service locator |
| API Versioning Strategy ADR | `docs/architecture/decisions/ADR-031-api-versioning-strategy.md` | 2026-02-10 | API versioning and evolution strategy |
| Middleware Stack ADR | `docs/architecture/decisions/ADR-032-middleware-stack.md` | 2026-02-10 | FastAPI middleware ordering and architecture |
| Config Consolidation ADR | `docs/architecture/decisions/ADR-033-config-consolidation.md` | 2026-02-10 | Configuration management consolidation |
| Enhanced Engine Routing ADR | `docs/architecture/decisions/ADR-034-enhanced-engine-routing.md` | 2026-02-11 | Enhanced engine routing and selection |
| **Sentinel Deterministic Workflow ADR** | `docs/architecture/decisions/ADR-035-sentinel-deterministic-workflow.md` | 2026-02-12 | 7-step sentinel workflow for reproducible pipeline validation |
| **Plugin System Unification ADR** | `docs/architecture/decisions/ADR-036-plugin-system-unification.md` | 2026-02-16 | Unified plugin architecture: single manifest schema, bridge service, permission model, phased implementation |
| **Plugin Trust Lane Model ADR** | `docs/architecture/decisions/ADR-037-plugin-trust-lane-model.md` | 2026-02-16 | Lane A (trusted in-process) for Phase 3; Lane B (isolated) deferred to Phase 4+ |
| **Plugin ABC Unification ADR** | `docs/architecture/decisions/ADR-038-plugin-abc-unification.md` | 2026-02-17 | Unified Plugin ABC with mixins; deprecates BasePlugin and PluginBase |
| **Phase 6 Strategic Maturity ADR** | `docs/architecture/decisions/ADR-039-phase6-strategic-maturity.md` | 2026-02-18 | Wasm runtime (wasmtime), AI quality, compliance, ecosystem, PQC research |
| **Dual Plugin Loader ADR** | `docs/architecture/decisions/ADR-040-dual-plugin-loader.md` | 2026-02-18 | Documents dual loader architecture (PluginLoader vs PluginService) and usage guidelines |
| **Model Lifecycle Strategy ADR** | `docs/architecture/decisions/ADR-043-model-lifecycle-strategy.md` | 2026-02-21 | Model registry, baselines, rollback, A/B testing integration |
| **Supply-Chain Integrity ADR** | `docs/architecture/decisions/ADR-044-supply-chain-integrity.md` | 2026-02-21 | Full-app SBOM, dependency hashes, installer provenance (Phase 12 WS2) |
| **MCP Integration Strategy ADR** | `docs/architecture/decisions/ADR-045-mcp-integration-strategy.md` | 2026-02-21 | MCP POC status, planned capabilities, roadmap (Arch Review 1.6) |
| **WinUI 3 XamlRoot Deferral ADR** | `docs/architecture/decisions/ADR-047-winui-xamlroot-deferral-pattern.md` | 2026-03-10 | Defer panel/overlay init to Loaded; never fire-and-forget XamlRoot-using async from Window constructor |
| **Centralized Request Coordination ADR** | `docs/architecture/decisions/ADR-048-centralized-request-coordination.md` | 2026-03-11 | IRequestCoordinator for single-flight, TTL, invalidation; BackendClient delegates; ProfilesViewModel simplified |

## Planning and Roadmaps

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Ultimate Master Plan 2026 (Optimized)** | `docs/governance/ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md` | 2026-02-04 | **ACTIVE PLAN** — 8 phases, 145 tasks, optimized role assignments. Supersedes prior plan versions. |
| **Unified Master Roadmap** | `docs/governance/MASTER_ROADMAP_UNIFIED.md` | 2026-01-25 | **Primary canonical roadmap** - consolidates all previous roadmaps |
| **Optional Task Inventory** | `docs/governance/OPTIONAL_TASK_INVENTORY.md` | 2026-01-29 | Authoritative optional-task backlog and dependency map; Phase 1 Master Plan deliverable |
| Master Roadmap (Legacy) | `docs/archive/governance/MASTER_ROADMAP.md` | 2026-01-25 | **ARCHIVED** — Superseded by MASTER_ROADMAP_UNIFIED.md. Note: `docs/archive/governance/` may be missing; create and move legacy roadmaps if archive policy requires. See [Final Sweep (Pre-Realignment)](../reports/audit/FINAL_SWEEP_ALL_ROLES_PRE_REALIGNMENT_2026-01-30.md) §2, §6.1. |
| Roadmap Summary (Legacy) | `docs/archive/governance/MASTER_ROADMAP_SUMMARY.md` | 2026-01-25 | **ARCHIVED** — Superseded by MASTER_ROADMAP_UNIFIED.md |
| Roadmap Index (Legacy) | `docs/archive/governance/MASTER_ROADMAP_INDEX.md` | 2026-01-25 | **ARCHIVED** — Superseded by MASTER_ROADMAP_UNIFIED.md |
| Task Tracking | `docs/governance/MASTER_TASK_CHECKLIST.md` | 2026-01-25 | **ARCHIVED** to `docs/archive/governance/` (GAP-DOC-003). Use `docs/tasks/` for active task briefs. |
| Task Log | `docs/governance/TASK_LOG.md` | 2026-01-25 | Historical task log |
| Phase Gates | `docs/governance/PHASE_GATES_EVIDENCE_MAP.md` | 2026-01-25 | Gate completion evidence |
| Risk Register | `docs/governance/RISK_REGISTER.md` | 2026-01-25 | Known risks and mitigations |
| Service Level Objectives | `docs/governance/SERVICE_LEVEL_OBJECTIVES.md` | 2026-01-25 | SLOs and telemetry-to-backlog integration |
| Architecture Integration Phase 4 Backlog | `docs/design/ARCHITECTURE_INTEGRATION_BACKLOG.md` | 2026-01-28 | R10/R11 done; R12 (skills-as-MCP) backlog |
| **Plugin Phase 3 Remediation Plan** | `docs/design/PLUGIN_PHASE3_REMEDIATION_PLAN.md` | 2026-02-16 | Findings and sprint plan from Phase 3 architectural review |
| **Cross-Role Escalation Matrix** | `docs/governance/CROSS_ROLE_ESCALATION_MATRIX.md` | 2026-01-29 | Decision tree and routing table for cross-role escalation; when to use Debug Agent vs other roles |
| **Handoff Protocol** | `docs/governance/HANDOFF_PROTOCOL.md` | 2026-01-29 | Standardized protocol for issue escalation and cross-role handoffs; templates and examples |

## References

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| API Reference | `docs/REFERENCE/` | 2026-01-25 | Consolidated API docs |
| Engine Reference | `docs/REFERENCE/ENGINE_REFERENCE.md` | 2026-01-25 | Engine capabilities and config |
| Engine Config | `backend/config/engine_config.json` | 2026-01-25 | Runtime engine configuration |
| **AutomationId Registry** | `docs/developer/AUTOMATION_ID_REGISTRY.md` | 2026-02-09 | Authoritative registry of stable AutomationIds; treat as public API; naming conventions and deprecation process |
| Overseer Reference | `docs/REFERENCE/OVERSEER_REFERENCE.md` | 2026-01-25 | Overseer tooling guide |
| Workers Reference | `docs/REFERENCE/WORKERS_REFERENCE.md` | 2026-01-25 | Worker system documentation |
| Project Status | `docs/REFERENCE/PROJECT_STATUS_REFERENCE.md` | 2026-01-25 | Current project status |
| Comprehensive Issues | `docs/REFERENCE/COMPREHENSIVE_ISSUES_REFERENCE.md` | 2026-01-25 | Known issues tracker |
| Storage Durability | `docs/REFERENCE/STORAGE_DURABILITY_REFERENCE.md` | 2026-01-27 | Atomic-write audit and reference (Role 4) |
| Job Runtime Map | `docs/REFERENCE/JOB_RUNTIME_MAP_REFERENCE.md` | 2026-01-27 | Job flows, cancellation, JobStateStore (Role 4) |
| Preflight | `docs/REFERENCE/PREFLIGHT_REFERENCE.md` | 2026-01-27 | Port 8001, intended use, plugin-dir (Role 4) |
| Artifact & Model | `docs/REFERENCE/ARTIFACT_MODEL_REFERENCE.md` | 2026-01-27 | Artifact/model storage, durability, preflight (Role 4) |

## Role Documentation

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Role Guides Index** | `docs/governance/ROLE_GUIDES_INDEX.md` | 2026-01-30 | Master index with phase-gate-role matrix, role ownership by module, invocation commands |
| Role 0: Overseer | `docs/governance/roles/ROLE_0_OVERSEER_GUIDE.md` | 2026-01-25 | Gate enforcement, evidence, coordination |
| Role 1: System Architect | `docs/governance/roles/ROLE_1_SYSTEM_ARCHITECT_GUIDE.md` | 2026-01-25 | Boundaries, contracts, ADRs |
| Role 2: Build & Tooling | `docs/governance/roles/ROLE_2_BUILD_TOOLING_GUIDE.md` | 2026-01-25 | Deterministic builds, CI/CD |
| Role 3: UI Engineer | `docs/governance/roles/ROLE_3_UI_ENGINEER_GUIDE.md` | 2026-01-25 | MVVM, VSQ tokens, WinUI 3 |
| Role 4: Core Platform | `docs/governance/roles/ROLE_4_CORE_PLATFORM_GUIDE.md` | 2026-01-25 | Runtime, storage, preflight |
| Role 5: Engine Engineer | `docs/governance/roles/ROLE_5_ENGINE_ENGINEER_GUIDE.md` | 2026-01-25 | Quality metrics, adapters |
| Role 6: Release Engineer | `docs/governance/roles/ROLE_6_RELEASE_ENGINEER_GUIDE.md` | 2026-01-25 | Installer, lifecycle, Gate H |
| **Role 7: Debug Agent** | `docs/governance/roles/ROLE_7_DEBUG_AGENT_GUIDE.md` | 2026-01-25 | Root-cause analysis, issue triage, system-wide fixes, validation |
| Skeptical Validator (subagent) | `docs/governance/SKEPTICAL_VALIDATOR_GUIDE.md` | 2026-01-28 | Cross-cutting validation subagent; §7 "When to Use" |
| Validator Escalation Protocol | `docs/governance/VALIDATOR_ESCALATION.md` | 2026-01-28 | Overseer queue, HIGH PRIORITY, escalation triggers |
| **Overseer Final Handoff** | `docs/governance/overseer/handoffs/OVERSEER_FINAL_HANDOFF.md` | 2026-02-18 | **NEW** — Successor handoff: architecture, risks, file map, verification playbook, recommendations (10 sections) |
| Context Manager Integration | `docs/governance/CONTEXT_MANAGER_INTEGRATION.md` | 2026-01-25 | Context manager architecture, ownership, and usage by role |
| Role Boundaries Protocol | `Recovery Plan/ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md` | 2026-01-25 | Role playbooks, handshake rules |
| Role Cheatsheet | `docs/developer/ROLE_CHEATSHEET.md` | 2026-01-25 | Quick one-liner prompts |

## Role System Prompts

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Role Prompts Index** | `.cursor/prompts/ROLE_PROMPTS_INDEX.md` | 2026-01-25 | Master index for all 7 role prompts |
| Role 0: Overseer Prompt | `.cursor/prompts/ROLE_0_OVERSEER_PROMPT.md` | 2026-01-25 | Complete system prompt for Overseer |
| Role 1: System Architect Prompt | `.cursor/prompts/ROLE_1_SYSTEM_ARCHITECT_PROMPT.md` | 2026-01-25 | Complete system prompt for System Architect |
| Role 2: Build & Tooling Prompt | `.cursor/prompts/ROLE_2_BUILD_TOOLING_PROMPT.md` | 2026-01-25 | Complete system prompt for Build & Tooling |
| Role 3: UI Engineer Prompt | `.cursor/prompts/ROLE_3_UI_ENGINEER_PROMPT.md` | 2026-01-25 | Complete system prompt for UI Engineer |
| Role 4: Core Platform Prompt | `.cursor/prompts/ROLE_4_CORE_PLATFORM_PROMPT.md` | 2026-01-25 | Complete system prompt for Core Platform |
| Role 5: Engine Engineer Prompt | `.cursor/prompts/ROLE_5_ENGINE_ENGINEER_PROMPT.md` | 2026-01-25 | Complete system prompt for Engine Engineer |
| Role 6: Release Engineer Prompt | `.cursor/prompts/ROLE_6_RELEASE_ENGINEER_PROMPT.md` | 2026-01-25 | Complete system prompt for Release Engineer |
| **Role 7: Debug Agent Prompt** | `.cursor/prompts/ROLE_7_DEBUG_AGENT_PROMPT.md` | 2026-01-25 | Complete system prompt for Debug Agent |
| Skeptical Validator Prompt | `.cursor/prompts/SKEPTICAL_VALIDATOR_PROMPT.md` | 2026-01-28 | Kickoff prompt for Skeptical Validator subagent (v1.1.0: role identity fix, validator_workflow.py integration, Quality Ledger clarification) |
| Onboarding Summary | `.cursor/prompts/ONBOARDING_COMPLETE_SUMMARY.md` | 2026-01-25 | Overseer onboarding completion report |

## Agent Skills

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Agent Skills | `.cursor/skills/` | 2026-01-28 | Role and tool skills for Cursor Agent |
| Skill Registration Script | `tools/skills/register_skill.ps1` | 2026-01-28 | Scaffold for new skills and templates |

## Developer Documentation

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Quick Start | `docs/governance/QUICK_START_GUIDE.md` | 2026-01-25 | Getting started for devs |
| README First | `docs/governance/README_FIRST.md` | 2026-01-25 | First-time contributor guide |
| Developer Guide | `docs/developer/DEVELOPER_GUIDE.md` | 2026-01-25 | Development practices |
| Build & Deploy | `docs/developer/BUILD_AND_DEPLOYMENT.md` | 2026-01-25 | Build process |
| Contributing | `docs/developer/CONTRIBUTING.md` | 2026-01-25 | Contribution guidelines |
| Onboarding | `docs/developer/ONBOARDING.md` | 2026-01-25 | New developer onboarding |
| Troubleshooting | `docs/developer/TROUBLESHOOTING.md` | 2026-01-25 | Common issues and fixes |
| Cursor User Rules | `docs/developer/CURSOR_USER_RULES.md` | 2026-01-25 | Global Cursor baseline for VoiceStudio |
| **Compatibility Matrix Guide** | `docs/developer/COMPATIBILITY_MATRIX_GUIDE.md` | 2026-02-02 | How to use and update the compatibility matrix; validation workflow |
| **AI Agent Development Guide** | `docs/developer/AI_AGENT_DEVELOPMENT_GUIDE.md` | 2026-02-02 | AI-assisted development best practices; scaffold usage, matrix checks |
| **Scaffolding Tools** | `tools/scaffolds/` | 2026-02-02 | CLI scaffolds: `generate_panel.py`, `generate_route.py`, `generate_engine.py` |
| **XAML Change Protocol** | `docs/developer/XAML_CHANGE_PROTOCOL.md` | 2026-02-04 | Mandatory procedures for XAML changes; forbidden patterns, binlog analysis workflow, Views subfolder protection |
| **UI Hardening Guidelines** | `docs/developer/UI_HARDENING_GUIDELINES.md` | 2026-02-04 | XAML stability best practices; UserControl extraction, ResourceDictionary organization, binding anti-patterns |
| **Phase 6 Developer Guide** | `docs/developer/PHASE6_DEVELOPER_GUIDE.md` | 2026-02-18 | Wasm plugins, AI quality, compliance, ecosystem, incubator features |
| **Plugin Privacy Guide** | `docs/developer/PLUGIN_PRIVACY_GUIDE.md` | 2026-02-18 | GDPR-inspired privacy framework; data categories, consent management, user rights |
| **Error Handling Guide** | `docs/developer/ERROR_HANDLING_GUIDE.md` | 2026-03-10 | Unified error envelope, error codes, severity levels, propagation patterns; schema: `shared/schemas/error-envelope.schema.json` (GAP-010 complete) |
| **WebSocket Guide** | `docs/developer/WEBSOCKET_GUIDE.md` | 2026-03-06 | WebSocket architecture, topics, message format, connection management; see `docs/REFERENCE/WEBSOCKET_TOPICS_REFERENCE.md` for topic reference (GAP-013 complete) |
| **UI Virtualization Guide** | `docs/developer/UI_VIRTUALIZATION_GUIDE.md` | 2026-02-04 | List virtualization patterns, incremental loading, performance guidelines (GAP-014) |
| **Command Palette Guide** | `docs/developer/COMMAND_PALETTE_GUIDE.md` | 2026-03-10 | IUnifiedCommandRegistry, CommandPaletteService, Ctrl+P, action types; GAP-015 complete |
| **Schema Sync Workflow** | `docs/developer/SCHEMA_SYNC.md` | 2026-02-11 | Schema ownership, validation, and synchronization workflow; shared/schemas/ governance |
| **Workspace manual test steps** | `docs/testing/WORKSPACE_MANUAL_TEST_STEPS.md` | 2026-02-12 | Manual verification steps for workspace dropdown and profile switching; optional Gate C note |
| **Sentinel Testing Guide** | `docs/developer/SENTINEL_TESTING_GUIDE.md` | 2026-02-12 | Sentinel workflow usage, configuration, test writing, debugging with repro packets |
| **UI Automation Guide** | `docs/developer/UI_AUTOMATION_GUIDE.md` | 2026-02-13 | **NEW** — WinAppDriver + Page Object Model, AutomationId standards, smoke tests, CI integration |
| **Architecture Foundations Guide** | `docs/developer/ARCHITECTURE_FOUNDATIONS_GUIDE.md` | 2026-02-14 | DI system, API versioning, caching layer, message queue, database migrations |
| **Scalability & Resilience Guide** | `docs/developer/SCALABILITY_RESILIENCE_GUIDE.md` | 2026-02-14 | Circuit breakers, rate limiting, retry logic, timeout config, horizontal scaling |
| **Production Readiness Guide** | `docs/operations/PRODUCTION_READINESS_GUIDE.md` | 2026-02-14 | Installer system, crash recovery, error handling, performance optimization, deployment checklist |
| **API Contract Tests** | `tests/integration/test_api_contracts.py` | 2026-02-14 | JSON Schema validation for API contracts against sentinel schemas |
| **Continuous Improvement Guide** | `docs/developer/CONTINUOUS_IMPROVEMENT_GUIDE.md` | 2026-02-14 | **NEW** — Feature flags, feedback collection, quality automation, documentation as code (Phase 8) |

## Reference Documentation

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Sentinel Contract Schemas** | `docs/REFERENCE/SENTINEL_CONTRACT_SCHEMAS.md` | 2026-02-12 | JSON Schema contracts for sentinel workflow; versioning policy, validation examples |
| **WebSocket Topics** | `docs/REFERENCE/WEBSOCKET_TOPICS_REFERENCE.md` | 2026-03-06 | Canonical topic reference for /ws/realtime; payloads, broadcast APIs; GAP-013 complete |

## Build and Diagnostic Tools

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Unified Verification Harness** | `scripts/verify.ps1` | 2026-02-09 | Single source of truth for product verification; 8 stages (build, lint, tests, contracts, integration, UI, gates); "no green = no merge" |
| **Engine Adapter Contract Tests** | `tests/contract/test_engine_adapter_contracts.py` | 2026-02-09 | Protocol compliance tests for all engine adapters; method signatures, error handling, device contracts |
| **AutomationId Validator** | `scripts/validate_automation_ids.py` | 2026-02-09 | Validates AutomationId registry against XAML files; detects drift between documentation and implementation |
| **XAML Compiler Playbook** | `docs/build/XAML_COMPILER_PLAYBOOK.md` | 2026-02-04 | Single operational runbook for XAML compiler troubleshooting; decision tree, copy-paste commands, emergency recovery |
| **XAML Diagnostic Build** | `scripts/build-with-binlog.ps1` | 2026-02-04 | Reproducible single-threaded build with binlog capture for XAML debugging |
| **Binlog Analysis (PS)** | `scripts/analyze-binlog.ps1` | 2026-02-04 | PowerShell script to extract XamlCompiler invocations and detect nested Views issues; supports file output |
| **Binlog Analysis (Python)** | `scripts/analyze_binlog.py` | 2026-02-04 | Python alternative for binlog analysis; supports file output for CI integration |
| **XAML Safety Rule** | `.cursor/rules/quality/xaml-safety.mdc` | 2026-02-04 | Cursor agent safety guardrails for XAML changes; forbidden patterns, non-destructive operations |
| **C#/WinUI Rule** | `.cursor/rules/languages/csharp-winui.mdc` | 2026-02-04 | MVVM conventions, XAML binding standards, command patterns for WinUI 3 development |
| **Proactive XAML Check** | `scripts/proactive-xaml-check.ps1` | 2026-02-04 | CI health check for XAML issues: nested Views, missing x:DataType, legacy bindings |

## Design and Specifications

> **Note on docs/design/**: This folder contains 60+ files from iterative development. The canonical sources are listed below. Files not listed are either:
> - **Superseded** by `docs/developer/ARCHITECTURE.md` (canonical architecture reference)
> - **Supplementary** reference material that may be archived in future
>
> When in doubt, prefer: `docs/developer/ARCHITECTURE.md` for architecture, ADRs for decisions, and this registry for all other canonicals.

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| UI Implementation | `docs/design/UI_IMPLEMENTATION_SPEC.md` | 2026-01-25 | UI design specification |
| Implementation Spec | `docs/design/VOICESTUDIO_COMPLETE_IMPLEMENTATION_SPEC.md` | 2026-02-11 | Full implementation spec; MCP sections updated to reflect current state |
| Execution Plan (Legacy) | `docs/archive/legacy_worker_system/design/EXECUTION_PLAN.md` | 2026-01-30 | **ARCHIVED** — Legacy Overseer+8-Worker plan; use MASTER_ROADMAP_UNIFIED and PROJECT_HANDOFF_GUIDE |
| File Structure | `docs/design/file-structure.md` | 2026-01-25 | Project file organization |
| Project Structure | `docs/design/project-structure.md` | 2026-01-25 | High-level project layout |
| **ViewModel DI Refactor** | `docs/design/viewmodel_di_refactor.md` | 2026-01-30 | TD-004; migration from AppServices/parameterless BaseViewModel to constructor injection; 4-phase rollout plan |
| **Engine Venv Isolation** | `docs/design/ENGINE_VENV_ISOLATION_SPEC.md` | 2026-01-30 | TD-001; per-engine/dual-venv strategy (Chatterbox vs XTTS torch); Option C (dual venv) recommended |
| **UI Automation** | `docs/design/UI_AUTOMATION_SPEC.md` | 2026-01-30 | Hybrid Gate C + WinAppDriver; Phase 2 Master Plan mini-spec; Option D (Hybrid) decision |
| Architecture Data Flow | `docs/design/ARCHITECTURE_DATA_FLOW.md` | — | **SUPERSEDED** by `docs/developer/ARCHITECTURE.md` |
| Architecture Diagrams | `docs/design/ARCHITECTURE_DIAGRAMS.md` | — | **SUPERSEDED** by `docs/developer/ARCHITECTURE.md` |
| Implementation Status | `docs/design/IMPLEMENTATION_COMPLETE.md`, `IMPLEMENTATION_STATUS.md`, etc. | — | **SUPERSEDED** — Historical snapshots; use STATE.md and MASTER_ROADMAP_UNIFIED for current status |
| Roadmaps (design/) | `docs/design/roadmap.md`, `PHASE_2_ROADMAP.md`, etc. | — | **SUPERSEDED** by `docs/governance/ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md` |

## Project Organization

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Organization Map | `docs/governance/PROJECT_ORGANIZATION_MAP.md` | 2026-01-25 | Project structure map |
| Reorg Log | `docs/governance/PROJECT_REORG_LOG.md` | 2026-01-25 | Reorganization history |
| Compatibility Matrix (Design) | `docs/design/COMPATIBILITY_MATRIX.md` | 2026-01-30 | Human-readable compatibility matrix; see also `config/compatibility_matrix.yml` |
| Production Build | `docs/governance/VoiceStudio_Production_Build_Plan.md` | 2026-01-25 | Production build plan |
| **Deterministic Sentinel Implementation Plan** | `docs/design/DETERMINISTIC_SENTINEL_IMPLEMENTATION_PLAN.md` | 2026-02-13 | 6-phase implementation plan for sentinel workflow, API hardening, UI automation, security/stability, architecture foundations, scalability |
| **Phase F Anti-Theater Hardening Plan** | `docs/design/PHASE_F_ANTI_THEATER_HARDENING_PLAN.md` | 2026-03-04 | Sprint plan: Gate C nav_steps, stub/real proof non-fakeability, STATE canonical, god-object budgets, schema/fingerprint alignment |
| **Dialog Architecture Bulletproof Plan** | `docs/design/DIALOG_ARCHITECTURE_BULLETPROOF_PLAN.md` | 2026-03-10 | Centralize ContentDialog/XamlRoot; IProfileDialogService, IXamlRootProvider; CI guard; Profiles-first template |

## Security

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Threat Model | `docs/reports/security/THREAT_MODEL.md` | 2026-01-25 | Baseline security threat model |
| **Security Configuration Guide** | `docs/operations/SECURITY_CONFIGURATION.md` | 2026-02-14 | **NEW** — Credential storage (DPAPI), error boundaries, health checks, graceful shutdown, correlation ID logging |

## Reports

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Gap Remediation Sprint Completion** | `docs/reports/audit/GAP_REMEDIATION_SPRINT_COMPLETION_2026-02-18.md` | 2026-02-18 | **NEW** — 52-task gap remediation completion report; Python lint fixes, verification harness GREEN |
| **Error Pattern Retrospective** | `docs/reports/post_mortem/ERROR_PATTERN_RETROSPECTIVE_2026-02-04.md` | 2026-02-04 | Comprehensive analysis of systemic behaviors causing recurring errors; 36 issues analyzed, role responsibility ranking, anti-pattern inventory |
| **Architecture Peer Review Package (Gate C / TASK-0004)** | `docs/reports/verification/ARCHITECTURE_PEER_REVIEW_PACKAGE_2026-01-27.md` | 2026-01-27 | Overseer-owned single entry point for architecture peer review; consolidates blockers, decisions, evidence, next tasks, approval map |
| **Complete Project Report (Start → 2026-02-02)** | `docs/reports/verification/VOICESTUDIO_COMPLETE_PROJECT_REPORT_2026-02-02.md` | 2026-02-02 | Single narrative + status + remaining gaps; links to SSOT; includes peer approval checklist |
| Session 11 Overseer Next Steps | `docs/reports/verification/SESSION11_OVERSEER_NEXT_STEPS_2026-01-27.md` | 2026-01-27 | Overseer run: tooling refresh, Task B deferred (venv), Task C partial (Install OK, Launch V1 fail); §6 peer approval |
| Peer Review Package | `docs/reports/verification/PEER_REVIEW_PACKAGE_2026-01-28.md` | 2026-01-28 | Peer review of items pending approval; tooling verification; Validator sign-off checklist |
| Gate C / Gate H Release Engineer | `docs/reports/packaging/GATE_C_H_RELEASE_ENGINEER_REPORT_2026-01-27.md` | 2026-01-27 | Gate C proof status, Gate H lifecycle plan, prereq gaps, evidence bundle |
| Rules Gap Analysis | `docs/reports/governance/RULES_GAP_ANALYSIS_REPORT.md` | 2026-01-25 | Rules Kit integration gap assessment |
| Rules Validation | `docs/reports/verification/RULES_VALIDATION_REPORT.md` | 2026-01-25 | Static validation + manual steps |
| UI Spec Reconciliation | `docs/reports/design/UI_SPEC_RECONCILIATION_MATRIX.md` | 2026-01-25 | Base vs Quantum+ comparison |
| UI Gap Analysis | `docs/reports/verification/UI_GAP_ANALYSIS_REPORT.md` | 2026-01-25 | Spec vs implementation gaps |
| **Accessibility Testing (Gate G)** | `docs/reports/verification/ACCESSIBILITY_TESTING_REPORT.md` | 2026-01-29 | Phase 4 QA; §2.1 formal screen reader procedure; Role 3 contribution |
| **Performance Testing (Phase 4)** | `docs/reports/verification/PERFORMANCE_TESTING_REPORT.md` | 2026-01-29 | Baseline UI/engine/SLO metrics; TASK-0014 Phase B |
| **Security Audit (Phase 4)** | `docs/reports/verification/SECURITY_AUDIT_REPORT.md` | 2026-01-29 | Dependency scan (pip-audit, dotnet vulnerable), code review; TASK-0014 Phase C |
| **Phase 5 Closure Report** | `docs/reports/packaging/PHASE_5_CLOSURE_REPORT_2026-01-29.md` | 2026-01-29 | Phase 5 (Packaging & Installer) formal closure; Gate H 1/1 GREEN; lifecycle 7/7 PASS; roadmap baseline complete; TASK-0017 deliverable |
| **Optional Tasks Master Plan — Stream Status** | `docs/reports/verification/ENGINE_PROOF_STREAM_STATUS_2026-01-29.md`, `CORE_PLATFORM_STREAM_STATUS_2026-01-29.md`, `UI_STREAM_STATUS_2026-01-29.md`, `BUILD_QUALITY_STREAM_STATUS_2026-01-29.md`, `OBSERVABILITY_STREAM_STATUS_2026-01-29.md` | 2026-01-30 | Phase 4/7/8 stream status: engine venv + baseline proofs; wizard upload + preflight; advanced panels + UI automation; build quality/warnings; SLO re-baseline + perf checks; Security Audit §9 CVE tracking |
| **TASK-0022 Evidence Pack** | `docs/reports/post_mortem/TASK-0022_EVIDENCE_PACK_2026-01-30.md` | 2026-01-30 | Enterprise-grade evidence catalog (E-001 to E-015), full missing file inventory (80+ files), minute-by-minute timeline for Git History Reconstruction incident |
| **Architecture Cross-Reference** | `docs/reports/verification/ARCHITECTURE_CROSS_REFERENCE_2026-01-30.md` | 2026-01-30 | Full 9-domain comparison matrix of ChatGPT specs vs implementation; gap analysis; actionable integration plan; TD-013 to TD-016 identified |
| **Comprehensive Documentation Audit** | `docs/reports/audit/COMPREHENSIVE_AUDIT_FINAL_REPORT_2026-01-30.md` | 2026-01-30 | 8-phase audit: specs extraction, codebase inventory, doc completeness, spec-to-code xref, architecture compliance, restored modules, gap analysis, final report; 10 deliverables |
| **Final Sweep — Gaps and Realignment** | `docs/reports/audit/FINAL_SWEEP_GAPS_AND_REALIGNMENT_2026-01-30.md` | 2026-01-30 | Pre-realignment sweep: missing canonicals (MASTER_ROADMAP_UNIFIED, PROJECT_HANDOFF_GUIDE, ROLE_GUIDES_INDEX, architecture Part*.md, 13 ADRs), role/workflow/architecture gaps, recommended order of operations |
| **Forensic System Report** | `docs/reports/forensic/VOICESTUDIO_FORENSIC_SYSTEM_REPORT_2026-01-30.md` | 2026-01-30 | Comprehensive forensic analysis: 38-day period, 136 verification reports (98.5% pass), 101 proof runs (59.4% with failures), TASK-0022 S0 incident, 5 RCAs, 9 recommendations, installer error, 4 crash dumps, security audit |
| **Final Sweep Before Realignment** | `docs/reports/audit/FINAL_SWEEP_BEFORE_REALIGNMENT_2026-01-30.md` | 2026-01-30 | All-roles final sweep: missing/misaligned canonical files (PROJECT_HANDOFF_GUIDE, MASTER_ROADMAP_UNIFIED, DOCUMENT_GOVERNANCE, ROLE_GUIDES_INDEX, architecture README/Part series, 12 ADRs), scaffolding/architecture/workflow/role gaps; checklist for realignment and roadmap update |
| **Final Sweep (Missing & Never-Done)** | `docs/reports/verification/FINAL_SWEEP_MISSING_AND_NEVER_DONE_2026-01-30.md` | 2026-01-30 | Cross-role audit: missing roadmap/ADRs/governance/handoff/task system/architecture/production; TASK-0022 outstanding; backend/frontend gaps; realignment checklist |
| **Final Sweep — One Last Time** | `docs/reports/audit/FINAL_SWEEP_ONE_LAST_TIME_2026-01-30.md` | 2026-01-30 | **Authoritative** all-roles sweep before realignment: verified present (19 ADRs, handoff, roadmap, PRODUCTION_READINESS, role guides, AppServices, UseCases); still missing (Domain/Infrastructure in App, ARCHIVE_POLICY, GOVERNANCE_LOCK, templates/, Part*.md, TASK-0009/0011–0019 briefs, commit-discipline.mdc, BRANCH_MERGE_POLICY); structures/layers/role checklist; realignment checklist (§5) |
| **Final Sweep — All Roles (Pre-Realignment)** | `docs/reports/audit/FINAL_SWEEP_ALL_ROLES_PRE_REALIGNMENT_2026-01-30.md` | 2026-01-30 | **Authoritative** pre-realignment sweep: corrects record (what exists post–TASK-0022 vs missing); still-missing canonicals (ARCHIVE_POLICY, GOVERNANCE_LOCK, RULE_PROPOSAL_TEMPLATE, Part*.md, docs/archive/governance/); implementation/architecture gaps; checklist for realignment and roadmap update (§6) |
| **Final Sweep — Consolidated for Realignment** | `docs/reports/audit/FINAL_SWEEP_CONSOLIDATED_FOR_REALIGNMENT_2026-01-30.md` | 2026-01-30 | **Single reference** for all roles before realignment: corrected record (what exists vs missing); still-missing files; implementation/architecture gaps; role expectations; realignment checklist (§6). Use for plan/roadmap/role update. |
| **Phase 5 Observability Audit** | `docs/reports/audit/PHASE5_OBSERVABILITY_AUDIT_2026-02-05.md` | 2026-02-05 | Phase 5 completion audit: 15/15 tasks complete; OpenTelemetry, trace propagation, SLO dashboard, Prometheus export, diagnostics, error tracking; gate_status PASS, ledger_validate PASS |
| **Phase 6 Security Audit** | `docs/reports/audit/PHASE6_SECURITY_AUDIT_2026-02-05.md` | 2026-02-05 | Phase 6 completion audit: 7/7 tasks complete; HMAC request signing (40 tests), file validation by magic bytes (58 tests), dependency policy, Dependabot config, SBOM generation, CVE monitoring workflow, secrets rotation guide; 98 tests PASS |
| **v1.0.1 Release Notes** | `docs/release/RELEASE_NOTES_v1.0.1.md` | 2026-02-05 | Phase 7 Production Readiness release: Installer enhancements (prerequisites, silent mode, upgrade validation), Error recovery (crash recovery, error reporting, data backup), Performance optimization (UI virtualization, lazy loading, response caching), Release documentation |
| **Core Workflow Audit** | `docs/reports/audit/CORE_WORKFLOW_AUDIT_2026-02-12.md` | 2026-02-12 | End-to-end workflow audit: Audio Import → Voice Cloning → Transcription → Playback; 35 issues (3 Critical, 7 High, 7 Medium, 5 Low, 6 Cross-workflow); 4-phase remediation roadmap; panel-by-panel feature matrix |
| **Panel Architecture Analysis** | `docs/reports/audit/PROFESSIONAL_PANEL_ARCHITECTURE_ANALYSIS.md` | 2026-02-14 | Moved from root (GAP-DOC-002). Professional-grade panel system analysis. |
| **Architecture Assessment and Remediation Plan** | `docs/reports/audit/ARCHITECTURAL_ASSESSMENT_AND_REMEDIATION_PLAN.md` | 2026-02-13 | Moved from root (GAP-DOC-002). Comprehensive architecture assessment. |
| **Complete System Report** | `docs/reports/VOICESTUDIO_COMPLETE_SYSTEM_REPORT.md` | 2026-02-13 | Moved from root (GAP-DOC-002). Full system capabilities report. |

## Release

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| **Release Notes (Main)** | `docs/release/RELEASE_NOTES.md` | 2026-02-16 | Moved from root (GAP-DOC-002). Primary release notes document. |
| **v1.0.2 Release Notes** | `docs/release/RELEASE_NOTES_v1.0.2.md` | 2026-02-21 | Plugin system infrastructure, Phase 4-10 deliverables. GA release. |
| **Handover Checklist** | `docs/release/HANDOVER_CHECKLIST.md` | 2026-02-21 | Build verification, test commands, gate status, key files, operational procedures, GA tag checklist |
| **v1.0.1 Release Notes** | `docs/release/RELEASE_NOTES_v1.0.1.md` | 2026-02-05 | Phase 7 Production Readiness release. |

## Overseer Tooling

| Topic | Canonical Source | Last Updated | Notes |
| --- | --- | --- | --- |
| Daily Workflow (Legacy) | `docs/archive/legacy_worker_system/overseer/DAILY_WORKFLOW_CHECKLIST.md` | 2026-01-30 | **ARCHIVED** — Legacy daily tasks; use ROLE_0_OVERSEER_GUIDE and PROJECT_HANDOFF_GUIDE |
| Gate Enforcement (Legacy) | `docs/archive/legacy_worker_system/overseer/GATE_ENFORCEMENT_GUIDE.md` | 2026-01-30 | **ARCHIVED** — Legacy gate guide; use Recovery Plan/QUALITY_LEDGER and run_verification.py |
| Handoff Process (Legacy) | `docs/archive/legacy_worker_system/overseer/HANDOFF_PROCESS_GUIDE.md` | 2026-01-30 | **ARCHIVED** — Legacy handoff; use HANDOFF_PROTOCOL.md and ROLE_GUIDES_INDEX |
| Quality Ledger | `Recovery Plan/QUALITY_LEDGER.md` | 2026-01-25 | VS-XXXX tracking |
| Verification automation | `scripts/run_verification.py`, `scripts/run-verification.ps1` | 2026-02-01 | Gate + ledger + completion guard (+ optional build, `--skip-guard` to bypass guard); proof in `.buildlogs/verification/last_run.json` |
| Overseer Issue System | `docs/developer/OVERSEER_ISSUE_SYSTEM.md` | 2026-02-02 | Unified issue logging from agents, engines, builds; recommendations and CLI for AI Overseer review; auto-task generation via `task_generator.py` |
| Issue-to-Task Generator | `tools/overseer/issues/task_generator.py` | 2026-02-02 | Automatic task brief creation from qualifying issues |
| Debug Agent Context Profile | `tools/context/config/roles/debug-agent.json` | 2026-02-02 | Context allocation weights/budgets for Debug Agent role |
| Telemetry API Routes | `backend/api/routes/telemetry.py` | 2026-02-02 | /api/telemetry/metrics, /api/telemetry/slos, /api/telemetry/spans endpoints |
| Onboarding Config | `tools/onboarding/config/onboarding.json`, `tools/onboarding/config/roles.json` | 2026-02-02 | Onboarding packet configuration and role registry |
| **Debug Role Integration** | `docs/developer/DEBUG_ROLE_INTEGRATION_GUIDE.md` | 2026-01-25 | Debug Role (Role 7) integration guide; issue-to-task workflow, escalation, CLI reference |

---

## Legacy Archive (Reference Only)

| Topic | Location | Notes |
| --- | --- | --- |
| Legacy Worker+Overseer System | `docs/archive/legacy_worker_system/` | 2026-01-30 — ChatGPT-era 3-Worker + Overseer docs; superseded by 8-role governance (ADR-003). See [README](../../archive/legacy_worker_system/README.md). |
| Outdated Dependency Status (PyTorch 2.9.0) | `docs/archive/dependencies/DEPENDENCY_STATUS_2025-01-28.md` | 2026-02-05 — References PyTorch 2.9.0+cu128 which was NOT adopted; production uses 2.2.2+cu121 per compatibility_matrix.yml. |

---

## Registry Maintenance

### Adding New Canonical Sources

1. Verify the topic doesn't already exist in this registry
2. Create the document following naming conventions in `DOCUMENT_GOVERNANCE.md`
3. Add an entry to the appropriate section above
4. Update the "Last Updated" date

### Superseding Documents

When a document is replaced:

1. Update this registry to point to the new canonical source
2. Add "Superseded by X" note to the old document
3. Move the old document to `docs/archive/{category}/`

### Disputes

If unclear which document is canonical:

1. Check this registry first
2. If not listed, check archive workflow in `docs/governance/DOCUMENT_GOVERNANCE.md` (ARCHIVE_POLICY.md not yet created; see [Final Sweep](../reports/audit/FINAL_SWEEP_ALL_ROLES_PRE_REALIGNMENT_2026-01-30.md) §6.1).
3. If still unclear, create an ADR to establish the canonical source
