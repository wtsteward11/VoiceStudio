# VoiceStudio Future Work Registry

> **Last Updated**: 2026-02-13 (Production Completion Audit)  
> **Owner**: Overseer (Role 0)  
> **Purpose**: Document deferred items, enhancement proposals, and future work for VoiceStudio

---

## Overview

This document tracks items that are intentionally deferred to future development phases. Items listed here are:
- Not blocking for v1.0.x releases
- Identified as valuable but lower priority
- Require additional research or dependencies
- May be revisited in future major versions

---

## Deferred Technical Debt

### TD-HIGH: High Priority Deferred Items

| ID | Description | Reason Deferred | Target Phase |
|----|-------------|-----------------|--------------|
| ~~TD-001~~ | ~~Chatterbox torch>=2.6 / per-engine venv~~ | **RESOLVED** via TD-015 venv families expansion (2026-02-10) | ~~Phase 11+~~ |
| ~~TD-002~~ | ~~Gate C Release full fix (revert NoWarn)~~ | **RESOLVED** - No CS0436/CS0618 suppressions; only CA1416 (legitimate) (2026-02-10) | ~~Phase 11+~~ |

### TD-MEDIUM: Medium Priority Deferred Items

| ID | Description | Reason Deferred | Target Phase |
|----|-------------|-----------------|--------------|
| ~~TD-003~~ | ~~protobuf CVE-2026-0994~~ | **RESOLVED** - protobuf 6.33.5 installed (CVE fixed at 5.28.3) (2026-02-10) | ~~Sprint 2~~ |
| ~~TD-004~~ | ~~ViewModel DI migration~~ | **RESOLVED** - All 75 ViewModels use IViewModelContext DI (2026-02-10) | ~~Phase 11+~~ |
| ~~TD-007~~ | ~~Warning count reduction~~ | **RESOLVED** - CI budget script at 2500; infrastructure complete (2026-02-10) | ~~Phase 11+~~ |

### TD-ARCHITECTURE: Architecture Gap Items

| ID | Description | Spec Source | Target Phase |
|----|-------------|-------------|--------------|
| ~~TD-013~~ | ~~VRAM Resource Scheduler~~ | **RESOLVED** - Per-engine VRAM budgets implemented (2026-02-10) | ~~Phase 11+~~ |
| ~~TD-014~~ | ~~Circuit Breaker pattern~~ | **RESOLVED** - Wired into voice, image, video, rvc routes (2026-02-10) | ~~Phase 11+~~ |
| ~~TD-015~~ | ~~Venv Families (8 families)~~ | **COMPLETE** - See `docs/design/VENV_FAMILIES_ANALYSIS.md` | ~~Phase 11+~~ |
| ~~TD-016~~ | ~~Engine Manifest Schema v2~~ | **RESOLVED** - verify_engine_tasks_targeted.py 4/4 PASS (2026-02-10) | ~~Phase 11+~~ |
| ~~TD-017~~ | ~~BaseEngine to EngineProtocol Migration~~ | **RESOLVED** - BaseEngine is alias for EngineProtocol (2026-02-11) | ~~v1.1+~~ |
| TD-018 | sys.path.insert for app/ imports | Routes (voice, training, synthesis, etc.) use sys.path.insert to import app.core.*. Fix: use EngineService/backend facade or make app a proper package. | v1.1+ |
| TD-019 | Training split-brain (PersistentStore + TrainingJobRepository) | Training uses both; pick one system of record. | v1.1+ |

#### TD-017: BaseEngine to EngineProtocol Migration — **COMPLETE**

**Resolution Date**: 2026-02-11

**Initial Finding**: Audit revealed that `BaseEngine` was a backward-compatible alias for `EngineProtocol`.

**Final Action (TASK-0050)**: 
- All 4 engine files now import `EngineProtocol` directly (already migrated)
- `BaseEngine` alias removed from `base.py`
- `BaseEngine` export removed from `__init__.py`
- Build verified: 0 errors
- Python imports validated: All 4 engines import successfully

---

## Deferred Features

### Engine Enhancements

| Feature | Description | Complexity | Notes |
|---------|-------------|------------|-------|
| ~~Bark Integration~~ | ~~Add Bark TTS engine with emotion control~~ | ~~Medium~~ | **COMPLETE** (2026-02-09) - Added SUPPORTED_EMOTIONS, EMOTION_PROMPTS, emotion parameter, get_supported_emotions() |
| ~~Tacotron 2~~ | ~~Classic TTS model integration~~ | ~~Low~~ | **COMPLETE** — `tacotron2_engine.py` with synthesize + synthesize_stream |
| ~~RVC v2~~ | ~~Voice conversion model upgrade~~ | ~~Medium~~ | **COMPLETE** — `engines/audio/rvc_v2/engine.manifest.json` + RVCEngine supports v2 |
| ~~Streaming Synthesis~~ | ~~Real-time audio streaming~~ | ~~High~~ | **COMPLETE** — `streaming_engine.py` with synthesize_stream + async streaming |

### External Tool Integrations (v1.1+ Roadmap)

| Feature | Description | Complexity | Target | Notes |
|---------|-------------|------------|--------|-------|
| ~~REAPER Project Import~~ | ~~Parse RPP files to extract audio tracks, markers, tempo~~ | ~~Medium~~ | v1.1+ | **IMPLEMENTED** (2026-02-12) – `daw_integration.py` ReaperIntegration.import_from_daw, RPP parser, tests in test_daw_integration.py |
| ~~Audacity Project Import~~ | ~~Parse AUP/AUP3 files to extract audio, labels~~ | ~~Medium~~ | v1.1+ | **IMPLEMENTED** (2026-02-12) – AudacityIntegration.import_from_daw, AUP3/AUP parsing, sampleblock export; tests in test_daw_integration.py |
| ~~DAW Export Presets~~ | ~~Pre-configured export settings per DAW~~ | ~~Low~~ | ~~v1.1+~~ | **IMPLEMENTED** (2026-02-12) – TD-038: DAW_EXPORT_PRESETS, GET /api/integrations/daw/presets, export accepts preset_id |

**Current State**: `/api/integrations/daw/*` endpoints work for basic export. REAPER (RPP) and Audacity (AUP3/AUP) project import are implemented in `backend/integrations/external/daw_integration.py`; use `open_project()` then `import_from_daw(project, track_index)` to get audio paths.

### UI Enhancements

| Feature | Description | Complexity | Notes |
|---------|-------------|------------|-------|
| ~~Advanced Animations~~ | ~~Fluent Design micro-interactions~~ | ~~Medium~~ | **COMPLETE** (2026-02-09) - LoadingOverlay fade, PanelHost/PanelStack transitions, VSQButton hover/press |
| ~~Theme Editor~~ | ~~User-customizable themes~~ | ~~Low~~ | **COMPLETE** (2026-02-11) - ThemeEditorViewModel, ColorPicker for custom accents, Theme.HighContrast.xaml, Theme.Default.xaml, accent persistence in ThemeManager |
| ~~Plugin Gallery~~ | ~~In-app engine/plugin browser~~ | ~~High~~ | **COMPLETE** (2026-02-11) - IPluginGateway, PluginGateway, PluginGalleryViewModel, PluginCard, PluginGalleryView, PluginDetailView panels registered |
| ~~Touch Optimization~~ | ~~Tablet-friendly UI adjustments~~ | ~~Medium~~ | **COMPLETE** (2026-02-09) - 44px targets, Density.Touch.xaml, Touch density mode |

### Phase 0 Controls (Implemented)

All seven controls now use WinUI Canvas/Path/Shapes for rendering (no Win2D). Binding surface and public API unchanged.

| Control | File | Purpose | Status |
|---------|------|---------|--------|
| MacroNodeEditorControl | `Controls/MacroNodeEditorControl.xaml.cs` | Node-based visual macro programming | Implemented |
| LoudnessChartControl | `Controls/LoudnessChartControl.xaml.cs` | Real-time loudness metering | Implemented |
| TrainingProgressChart | `Controls/TrainingProgressChart.xaml.cs` | Training loss/quality visualization | Implemented |
| RadarChartControl | `Controls/RadarChartControl.xaml.cs` | Multi-axis quality comparison | Implemented |
| AutomationCurveEditorControl | `Controls/AutomationCurveEditorControl.xaml.cs` | Single automation curve editing | Implemented |
| AutomationCurvesEditorControl | `Controls/AutomationCurvesEditorControl.xaml.cs` | Multi-curve editing | Implemented |
| EnsembleTimelineControl | `Controls/EnsembleTimelineControl.xaml.cs` | Ensemble timeline visualization | Implemented |

### ~~Coming Soon APIs~~ (Resolved)

These endpoints have been resolved as of 2026-02-13 (Production Completion Plan Phase 1):

| Endpoint | Feature | Resolution |
|----------|---------|------------|
| `/api/integrations/sync/start` | Cloud Sync | Returns `local_only` — local-first policy (ADR-010). Use `/api/backup` for project transfer. |
| `/api/integrations/workflows/start` | Workflow Automation | **IMPLEMENTED** — dispatches to job queue; supports batch_synthesis and custom workflows. |
| `/api/integrations/batch/start` | Batch Processing | **IMPLEMENTED** — creates queued jobs with progress tracking via `/api/jobs/{id}`. |

### Infrastructure

| Feature | Description | Complexity | Notes |
|---------|-------------|------------|-------|
| Cross-Platform | macOS/Linux via Avalonia/MAUI | Very High | Major architecture change |
| ~~Cloud Sync~~ | ~~Optional cloud backup for profiles~~ | ~~Medium~~ | Moved to **Coming Soon APIs (v1.2)** |
| ~~CI/CD Pipeline~~ | ~~Automated build and deployment~~ | ~~Medium~~ | **COMPLETE** — 7 workflows in `.github/workflows/` (ci, build, test, release, security-monitor, sbom, governance) |
| ~~Telemetry (Opt-in)~~ | ~~Usage analytics for improvement~~ | ~~Low~~ | **COMPLETE** (2026-02-09) - AnalyticsService consent, TelemetryConsentDialog, SettingsView privacy section, PRIVACY_POLICY.md |

### MCP Integration Roadmap (Future)

**Status**: Proof-of-concept only. See ADR-049.

**Current State**: `backend/mcp_bridge/` contains only `pdf_unlocker_client.py` for PDF unlock functionality. No MCP dashboard, no full orchestration.

**Target State**: Full MCP integration for design tokens, AI model calls, and engine orchestration.

| Phase | Feature | Description | Complexity | Target |
|-------|---------|-------------|------------|--------|
| MCP-1 | Unified MCP Client | Create `backend/mcp_bridge/mcp_client.py` with operation routing | Medium | v1.2 |
| MCP-2 | Design Token Sync | MCP client for Figma/design token servers | Medium | v1.2 |
| MCP-3 | AI Model Calls | Route synthesis operations through MCP | High | v1.3 |
| MCP-4 | Engine Discovery | MCP-based engine capability discovery | Medium | v1.3 |

**Prerequisites**:
- Define MCP operation contracts in `shared/contracts/`
- Create MCP server adapter pattern
- Document MCP security considerations (see `mcp-security.mdc`)

**Related Files**:
- `backend/mcp_bridge/README.md` — MCP bridge documentation
- `docs/design/VOICESTUDIO_COMPLETE_IMPLEMENTATION_SPEC.md` — Target architecture (§MCP Integration Hooks)

---

## Documentation Backlog

### ADR Status

**COMPLETE (2026-02-10)**: All 30 ADRs (ADR-001 through ADR-030) have been formally documented with Accepted status. See [docs/architecture/decisions/README.md](../architecture/decisions/README.md) for the full index.

### Missing Task Briefs

**COMPLETE (2026-02-10)**: All previously missing task briefs have been backfilled:

- [x] TASK-0009 — Engine Integration (Piper/Chatterbox Baseline)
- [x] TASK-0011 — Engine Engineer Venv Restore / Baseline Proof
- [x] TASK-0012 — Governance Cleanup Sprint
- [x] TASK-0013 — Phase 2 Follow-up
- [x] TASK-0014 — Phase 4 QA Completion
- [x] TASK-0015 — Gate C Release Build
- [x] TASK-0016 — Phase 4 Dependencies
- [x] TASK-0017 — Phase 5 Closure / Roadmap Baseline Complete
- [x] TASK-0018 — TD-006 Closure (Ledger Warnings Documentation)
- [x] TASK-0019 — Phase 2+ Next-Work Selection
- [x] TASK-0023 — Interface Implementations + Pre-commit Hooks
- [x] TASK-0026 — Introduce Engine Interface Layer
- [x] TASK-0021 — OpenMemory MCP Wiring (**COMPLETE** 2026-02-11)

---

## Research Topics

Items requiring research before implementation:

| Topic | Question | Priority |
|-------|----------|----------|
| WebGPU TTS | Can TTS run in browser via WebGPU? | Low |
| Whisper.cpp Integration | Worth replacing Python Whisper? | Medium |
| ONNX Runtime | Benefits of ONNX conversion for engines | Medium |
| Model Quantization | INT8/INT4 quantization for smaller models | Medium |

---

## Dependencies on External Projects

Items blocked on external project updates:

| Item | Dependency | Status |
|------|------------|--------|
| Torch 2.6+ features | PyTorch release | Waiting |
| WinUI 3.x updates | Microsoft Windows App SDK | Monitoring |
| Python 3.13+ features | Python release | Monitoring |
| Transformers API changes | Hugging Face | Monitoring |

---

## Contribution Guidelines

To add items to this document:

1. **Categorize appropriately** — Tech Debt, Features, Documentation, Research
2. **Include rationale** — Why is this deferred?
3. **Set target phase** — When should this be revisited?
4. **Link related items** — Reference ADRs, issues, or other docs

---

## References

- [TECH_DEBT_REGISTER.md](TECH_DEBT_REGISTER.md) — Active tech debt tracking
- [MASTER_ROADMAP_UNIFIED.md](MASTER_ROADMAP_UNIFIED.md) — Project roadmap
- [OPTIONAL_TASK_INVENTORY.md](OPTIONAL_TASK_INVENTORY.md) — Optional enhancements
- [docs/architecture/decisions/](../architecture/decisions/) — ADR index

---

*Future work registry for VoiceStudio Quantum+. Created 2026-02-10 as part of Phase 10 Documentation Completeness.*
