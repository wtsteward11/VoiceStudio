# VoiceStudio Technical Debt Register

> **Last Updated**: 2026-02-13  
> **Owner**: Overseer (Role 0)  
> **Purpose**: Canonical registry of all known technical debt, limitations, and future enhancements

---

## Active Technical Debt

| ID | Title | Priority | Added | Category | Description |
|----|-------|----------|-------|----------|-------------|
| — | (none) | — | — | — | All technical debt closed as of 2026-02-28 |

---

## Closed Technical Debt

| ID | Title | Closed Date | Resolution | Proof |
|----|-------|-------------|------------|-------|
| **TD-006** | Ledger warnings | 2026-01-29 | Documented as expected warnings | TASK-0018 |
| **TD-007** | Warning count | 2026-02-02 | Reduced 4990→2046 (54%); CI budget added at 2500 | scripts/check_warning_budget.py, build.yml |
| **TD-008** | Git History Reconstruction | 2026-01-30 | 11 recovery commits, 80+ files recovered | TASK-0022 |
| **TD-009** | Commit Discipline Enforcement | 2026-02-02 | Pre-commit hooks verified working | pre-commit run --all-files |
| **TD-014** | Circuit Breaker Pattern | 2026-02-02 | CircuitBreaker wired into image_gen, video_gen, rvc routes | backend/services/circuit_breaker.py |
| **TD-016** | Engine Manifest Schema v2 | 2026-02-02 | verify_engine_tasks_targeted.py 4/4 PASS | ENGINE_ENGINEER_STATUS_2026-02-01 |
| **TD-005** | Wizard e2e proof | 2026-02-02 | Wizard flow proof 4/4 PASS, 2 SKIP (engine deps) | .buildlogs/proof_runs/wizard_flow_20260201-231924 |
| **TD-017** | OpenAPI spec regeneration | 2026-02-02 | docs/api/openapi.json regenerated with 508 paths | curl http://localhost:8002/openapi.json |
| **TD-003** | Python CVE: protobuf | 2026-02-02 | protobuf 6.33.4 installed (CVE fixed at 5.28.3) | pip show protobuf |
| **TD-002** | Release build suppressions | 2026-02-02 | Release build 0 warnings, 0 errors; no suppressions needed | dotnet build -c Release |
| **TD-004** | ViewModel DI migration | 2026-02-02 | All 68 ViewModels use IViewModelContext DI; legacy constructor unused | Grep: IViewModelContext |
| **TD-011** | Interface Implementations | 2026-02-02 | ViewModelContext, TelemetryServiceStub, JsonProjectRepository implemented | Grep: class.*: I*Service |
| **TD-013** | VRAM Resource Scheduler | 2026-02-02 | Per-engine VRAM budgets, eviction policy, priority-based preemption | tests/unit/core/runtime/test_resource_manager.py (11/11 PASS) |
| **TD-015** | Venv Families Strategy | 2026-02-02 | VenvFamilyManager, 3 families, 10 engine manifests, requirements files | tests/unit/core/runtime/test_venv_family_manager.py (14/14 PASS) |
| **TD-001** | Chatterbox torch+SM120 | 2026-02-02 | Mitigated via TD-015 venv families; venv_advanced_tts with torch 2.6.0+cu124 | proof_runs/chatterbox_baseline_20260202_080607 (4/4 PASS) |
| **TD-012** | Namespace Cleanup | 2026-02-02 | UseCases namespace correctly defined and used; no issues found | Grep: App.UseCases |
| **TD-010** | Branch Merge Policy | 2026-02-02 | Policy created: docs/governance/BRANCH_MERGE_POLICY.md | BRANCH_MERGE_POLICY.md |
| **TD-018** | Empty Catch Remediation | 2026-02-04 | 118 C# + 57 Python remediated; ErrorLogger.LogWarning / allowlist | fix_empty_catches.py, fix_bare_excepts.py |
| **TD-019** | Python Path Standardization | 2026-02-04 | 27 scripts use _env_setup.py; PROJECT_ROOT pattern | Grep: _env_setup |
| **TD-020** | XAML Safety Tooling | 2026-02-04 | Infrastructure complete | Build stability |
| **TD-021** | Observability Infrastructure | 2026-02-04 | DiagnosticsPanel correlation ID, copy, search | Role 4 |
| **TD-022** | Contract Validation | 2026-02-04 | 44 engine manifests with contract specs | add_engine_contracts.py |
| **TD-023** | Route Boundary Violations | 2026-02-04 | 37 violations fixed; EngineService extended | 20 files |
| **TD-024** | Static ServiceProvider Calls | 2026-02-04 | 47 static calls migrated to AppServices.TryGetXxx | migrate_di.py 0 issues |
| **TD-025** | ADR Formalization | 2026-02-04 | All 22 ADRs have formal decisions | ADR-017, ADR-008, ADR-011 |
| **TD-026** | Route Ordering Fixes | 2026-02-09 | shortcuts.py parameterized routes fixed | Test reliability |
| **TD-027** | Build Warning Remediation | 2026-02-09 | CS0108, CS1998, CS0168, BackendClient fix | dotnet build |
| **TD-028** | Test Fixture Improvements | 2026-02-09 | Mixer test state; shortcut skip decorators removed | Test coverage |
| **TD-029** | Mock Translation Removal | 2026-02-12 | voice/translation/engine.py returns original + warning | User clarity |
| **TD-030** | Sample Data Removal | 2026-02-12 | PluginGateway, VoiceProfileViewModel, SLODashboardViewModel | Data integrity |
| **TD-031** | 501 Endpoint Fixes | 2026-02-12 | feedback.py, search.py, todo_panel.py | API reliability |
| **TD-032** | Engine Placeholder Updates | 2026-02-12 | s2s_translator, rvc_realtime pass-through | User clarity |
| **TD-033** | Centralized Config Verification | 2026-02-12 | app_config.py, AppConfig.cs verified | Configuration |
| **TD-034** | Deferred UI Controls | 2026-02-12 | 7 controls implemented (Canvas/Path/Shapes); no Win2D | Controls/*.xaml.cs |
| **TD-035** | DAW Project Import | 2026-02-12 | REAPER RPP + Audacity AUP3/AUP in daw_integration.py | test_daw_integration.py 14/14 PASS |
| **TD-036** | Workspace automated UI smoke step | 2026-02-12 | Gate C workspace switch + assert in MainWindow | ui_smoke_steps_latest.log |
| **TD-037** | WhisperX Engine Implementation | 2026-02-12 | WhisperXEngine in app/core/engines/whisperx_engine.py; manifest engines/audio/whisperx; TranscribeViewModel.Engines includes "whisperx"; transcribe route supports diarization | whisperx_engine.py, transcribe.py, TranscribeViewModel.cs |
| **TD-038** | DAW Export Presets | 2026-02-12 | Pre-configured presets in daw_integration.py (DAW_EXPORT_PRESETS, get_daw_export_presets, get_daw_export_preset_by_id); GET /api/integrations/daw/presets; export accepts preset_id | daw_integration.py, integrations.py, test_daw_integration.py |
| **TD-039** | Dynamic Transcription Engine Discovery | 2026-02-28 | Implemented in TranscribeViewModel.cs via GAP-CS-003: LoadEnginesAsync() calls GetTranscriptionEnginesAsync(), populates Engines dynamically, falls back to hardcoded list on backend error | TranscribeViewModel.cs lines 229-280 |
| **TD-040** | Starlette Double call_next AssertionError | 2026-03-03 | Fixed double call_next in performance_profiling_middleware and request_size_limit_middleware except blocks; replaced with raise. Added Unicode control character path rejection in InputValidationMiddleware (C-T1) | tests/unit/test_middleware_regression.py (8 pass) |
| **TD-041** | Test Isolation (Order-Dependent Tests) | 2026-03-03 | Installed pytest-randomly; added --randomly-seed=last to pytest.ini; autouse fixtures clear module-level job stores (conftest.py) and performance middleware metrics (integration/conftest.py); CI runs two seeds (C-T2) | ci.yml dual-seed runs |

---

## Technical Debt by Category

### Architecture Gaps (from ChatGPT Spec Cross-Reference)

| TD ID | ChatGPT Spec Section | Gap Description | ADR Reference |
|-------|---------------------|-----------------|---------------|
| TD-013 | Part 7: Resource Management | VRAM Resource Scheduler not implemented | — |
| TD-014 | Part 3: Orchestration | Circuit Breaker pattern missing | — |
| TD-015 | Part 4: Engine Layer | Venv Families not implemented | — |
| TD-016 | Part 4: Engine Layer | Engine Manifest Schema v2 not adopted | — |
| — | Part 5: IPC | Named Pipes replaced with HTTP | ADR-018 |
| — | Part 3: Orchestration | C# orchestration in Python instead | ADR-019 |

### Build & Process

| TD ID | Category | Description |
|-------|----------|-------------|
| TD-002 | Build | Release NoWarn suppressions |
| TD-007 | Build | High warning count |
| TD-009 | Process | Commit discipline enforcement |
| TD-010 | Process | Branch merge policy |
| TD-017 | Documentation | Stale OpenAPI spec |

### Code Quality

| TD ID | Category | Description |
|-------|----------|-------------|
| TD-004 | DI Migration | ViewModel DI incomplete |
| TD-011 | Interfaces | Missing implementations |
| TD-012 | Namespaces | Wrong namespace references |

### Dependencies

| TD ID | Category | Description |
|-------|----------|-------------|
| TD-001 | Engine Deps | Chatterbox torch version |
| TD-003 | Security | protobuf CVE |

---

## Tech Debt to Task Mapping

| TD ID | TASK ID | Status |
|-------|---------|--------|
| TD-005 | TASK-0020 | In Progress |
| TD-006 | TASK-0018 | Complete |
| TD-008 | TASK-0022 | Complete |
| TD-009 | TASK-0023 | Pending |
| TD-011 | TASK-0023 | Pending |
| TD-013 | TASK-0028 | Proposed |
| TD-015 | TASK-0029 | Proposed |

---

## Mitigation Strategies

### TD-001: Chatterbox torch version

**Options:**
1. Upgrade venv torch to 2.6 (risk: breaks XTTS)
2. Separate venv for Chatterbox (aligns with TD-015)
3. Defer until TD-015 venv families implemented

**Recommendation:** Option 3 — defer until venv families strategy resolved

### TD-013: VRAM Resource Scheduler

**Implementation Plan:**
1. Create `ResourceScheduler` class in backend
2. Track VRAM allocation per engine
3. Implement priority queue for jobs
4. Add eviction policy for low-priority allocations
5. Wire into engine lifecycle

**Effort:** 16-24 hours

### TD-014: Circuit Breaker Pattern

**Implementation Plan:**
1. Create `CircuitBreaker` class with states (Closed, Open, HalfOpen)
2. Track failure counts per engine
3. Open circuit after 3 failures
4. Auto-reset after recovery timeout
5. Wire into engine manager

**Effort:** 8-12 hours

### TD-015: Venv Families Strategy

**Implementation Plan:**
1. Analyze 49 engines for dependency compatibility
2. Group into 10-12 families
3. Create per-family requirements.txt
4. Update engine manager to use family venvs
5. Update installer to bundle venvs

**Effort:** 40-60 hours (major initiative)

---

## Process Improvements (from TASK-0022 Lessons Learned)

### Commit Discipline Rule (TD-009)

**Rule:** Tasks are NOT complete until committed to git.

**Enforcement:**
- Pre-commit hook validates STATE.md changes
- Rule file: `.cursor/rules/workflows/commit-discipline.mdc`
- Weekly audit for uncommitted work

### Branch Merge Policy (TD-010)

**Policy:**
- Max divergence: 10 commits OR 2 weeks
- Mandatory review at 20 commits
- Process violation at 50 commits

**Enforcement:**
- Weekly `git log master..branches` audit
- Document: `docs/governance/BRANCH_MERGE_POLICY.md`

---

## References

- [ARCHITECTURE_CROSS_REFERENCE_2026-01-30.md](../reports/verification/ARCHITECTURE_CROSS_REFERENCE_2026-01-30.md)
- [TASK-0022_COMPLETE_RECOVERY_REPORT_2026-01-30.md](../reports/post_mortem/TASK-0022_COMPLETE_RECOVERY_REPORT_2026-01-30.md)
- [ADR-018](../architecture/decisions/ADR-018-named-pipes-http.md) (Named Pipes over HTTP)
- [ADR-019](../architecture/decisions/ADR-019-orchestration-architecture.md) (Orchestration Deviation)

---

## Changelog

| Date | Change |
|------|--------|
| 2026-01-30 | Created register; added TD-001 through TD-016 |
| 2026-01-30 | Added Architecture Gaps from cross-reference analysis |
| 2026-01-30 | Closed TD-006 (TASK-0018) and TD-008 (TASK-0022) |
| 2026-02-02 | Added TD-017 (OpenAPI spec regeneration); Closed TD-016 (Engine Manifest verified) |
| 2026-02-02 | Closed TD-007 (warning reduction 54%), TD-009 (pre-commit verified), TD-014 (circuit breaker wired) |
| 2026-02-02 | Closed TD-017 (OpenAPI spec regenerated with 508 paths from running backend) |
| 2026-02-02 | Closed TD-005 (Wizard e2e proof 4/4 PASS, 2 SKIP) |
| 2026-02-04 | Added TD-018 through TD-022 from Phase 7 Quality Infrastructure plan |
| 2026-02-04 | Closed TD-020 (XAML Safety Tooling infrastructure complete) |
| 2026-02-12 | Register hygiene: moved all CLOSED items (TD-001–TD-036) to Closed section; Active contained only TD-037 (WhisperX) |
| 2026-02-12 | Closed TD-034 (7 UI controls implemented), TD-035 (DAW import), TD-036 (workspace smoke); added resolution rows to Closed table |
| 2026-02-12 | Closed TD-037 (WhisperX engine): whisperx_engine.py, manifest engines/audio/whisperx, TranscribeViewModel.Engines + "whisperx", transcribe route diarization; Active table now empty |
| 2026-02-12 | TD-038 DAW Export Presets: implemented DAW_EXPORT_PRESETS, get_daw_export_presets, get_daw_export_preset_by_id; GET /daw/presets; export preset_id; tests in test_daw_integration.py; closed same day |
| 2026-03-03 | TD-040 Starlette Double call_next AssertionError (C-T1): fixed double call_next, added Unicode control char guard |
| 2026-03-03 | TD-041 Test Isolation (C-T2): pytest-randomly installed, autouse fixtures for state cleanup, CI dual-seed runs |