# Gap Analysis & Remediation Plan

> **Generated**: 2026-01-30
> **Phase**: 7 - Gap Analysis & Remediation Planning
> **Status**: Complete

---

## Executive Summary

This document consolidates all gaps identified across audit phases and provides a prioritized remediation plan.

**Total Gaps Identified**: 28
**Critical**: 3
**High Priority**: 8
**Medium Priority**: 10
**Low Priority**: 7

---

## 1. Gap Summary by Phase

### Phase 3: Documentation Gaps

| ID | Gap | Severity | Phase |
|----|-----|----------|-------|
| DOC-001 | 13 ADR files missing from filesystem | CRITICAL | 3 |
| DOC-002 | Unified Error Envelope not documented | MEDIUM | 3 |
| DOC-003 | WebSocket Topics documentation incomplete | MEDIUM | 3 |
| DOC-004 | UI Virtualization not documented | LOW | 3 |
| DOC-005 | Command Palette documentation incomplete | LOW | 3 |

### Phase 4: Implementation Gaps

| ID | Gap | Severity | Phase |
|----|-----|----------|-------|
| IMP-001 | Unified Error Envelope not fully standardized | MEDIUM | 4 |
| IMP-002 | HasError not in BaseViewModel | MEDIUM | 4 |
| IMP-003 | UI Virtualization not universal | LOW | 4 |
| IMP-004 | Short-term memory sliding window not fully implemented | MEDIUM | 4 |

### Phase 5: Architecture Gaps

| ID | Gap | Severity | Phase |
|----|-----|----------|-------|
| ARCH-001 | Routes import engines directly (23 files) | HIGH | 5 |
| ARCH-002 | FastAPI import in services layer | MEDIUM | 5 |
| ARCH-003 | Routes import engine utilities | MEDIUM | 5 |
| ARCH-004 | Business logic in View code-behind | HIGH | 5 |
| ARCH-005 | 5 ViewModels don't inherit BaseViewModel | HIGH | 5 |
| ARCH-006 | Direct HttpClient instantiation | HIGH | 5 |
| ARCH-007 | AppServices anti-pattern in Views | HIGH | 5 |
| ARCH-008 | Direct WebSocket client instantiation | MEDIUM | 5 |
| ARCH-009 | Contract validation not enforced at all boundaries | LOW | 5 |

### Cross-Cutting Gaps

| ID | Gap | Severity | Phase |
|----|-----|----------|-------|
| CC-001 | Missing engine interface layer | HIGH | 5 |
| CC-002 | No DI container for ViewModel resolution | HIGH | 5 |
| CC-003 | Import linting rules not enforced | LOW | 5 |

---

## 2. Critical Gaps (Must Fix)

### GAP-001: 13 Missing ADR Files [CRITICAL] ✅ COMPLETE

**Description**: CANONICAL_REGISTRY references 13 ADR files that do not exist in `docs/architecture/decisions/`.

**Status**: COMPLETE (2026-02-10) - Verified via glob search: All 34 ADR files now exist in `docs/architecture/decisions/`.

**Impact**: Documentation integrity compromised. References to non-existent files.

**Missing Files**:
1. ADR-002-document-governance.md
2. ADR-004-messagepack-ipc.md
3. ADR-005-context-management-system.md
4. ADR-006-enhanced-cursor-rules-system.md
5. ADR-007-ipc-boundary.md
6. ADR-008-architecture-patterns.md
7. ADR-009-ai-native-development-patterns.md
8. ADR-010-native-windows-platform.md
9. ADR-011-context-manager-architecture.md
10. ADR-012-roadmap-integration-scaffolding.md
11. ADR-013-opentelemetry-distributed-tracing.md
12. ADR-014-agent-skills-integration.md
13. ADR-016-task-classifier-and-mcp-selector.md

**Remediation Options**:
1. **Option A**: Create all 13 ADR files with proper content (HIGH EFFORT)
2. **Option B**: Update CANONICAL_REGISTRY to remove references (LOW EFFORT)
3. **Option C**: Create placeholder ADRs marked "PENDING" (MEDIUM EFFORT)

**Recommendation**: Option C - Create placeholder ADRs with TODO status

**Estimated Effort**: 4-6 hours

---

### GAP-002: Routes Import Engines Directly [HIGH] ✅ COMPLETE

**Description**: 23 route files import directly from `app.core.engines` instead of using abstractions.

**Impact**: Violates Clean Architecture dependency direction. Couples interface adapters to infrastructure.

**Status**: COMPLETE (2026-02-10) - Addressed via TD-023 (Route Boundary Violations, CLOSED). LLMProviderService abstraction created, architecture boundary checks added to pre-commit hooks.

**Affected Files**:
- `backend/api/routes/voice.py`
- `backend/api/routes/transcribe.py`
- `backend/api/routes/video_gen.py`
- Plus 20 more route files

**Remediation**:
1. Create `backend/interfaces/` or `backend/ports/` for engine abstractions
2. Define interfaces: `ITTSEngine`, `ISTTEngine`, `IImageEngine`, etc.
3. Inject implementations via dependency injection
4. Refactor routes to use interfaces

**Estimated Effort**: 16-24 hours

---

### GAP-003: DI Container Missing for ViewModels [HIGH] ✅ COMPLETE

**Description**: ViewModels are instantiated with `AppServices.Get...()` anti-pattern in Views.

**Impact**: Violates MVVM. Couples Views to service resolution. Harder to test.

**Status**: COMPLETE (2026-02-10) - Addressed via TD-004 (ViewModel DI migration, CLOSED) and TD-024 (Static ServiceProvider Calls, CLOSED). ViewModels now use `AppServices.GetViewModelContext()` pattern for DI.

**Current Pattern**:
```csharp
// Anti-pattern in Views
ViewModel = new ProfilesViewModel(
    AppServices.GetBackendClient(),
    AppServices.GetProfilesUseCase(),
    ...);
```

**Remediation**:
1. Configure DI container to resolve ViewModels
2. Update View code-behind to use DI resolution
3. Remove `AppServices.Get...()` calls

**Estimated Effort**: 8-12 hours

---

## 3. High Priority Gaps

### GAP-004: Business Logic in View Code-Behind [HIGH] ✅ COMPLETE

**Files Affected**:
- `ProfilesView.xaml.cs`
- `TrainingView.xaml.cs`
- `VoiceSynthesisView.xaml.cs`

**Remediation**: Move business logic to ViewModels

**Estimated Effort**: 4-6 hours

**Status**: COMPLETE (2026-02-10)
- TrainingView.xaml.cs: `ExportDatasetAsync` and `ExportTrainingJobAsync` serialization logic moved to `TrainingViewModel.GetExportDatasetContent()` and `GetExportTrainingJobContent()`
- ProfilesView.xaml.cs and VoiceSynthesisView.xaml.cs: Reviewed - existing code appropriately delegates to ViewModel commands, only UI orchestration (dialogs, toasts) remains in View

---

### GAP-005: ViewModels Not Inheriting BaseViewModel [HIGH] ✅ COMPLETE

**Files Affected**:
- `ProfilesViewModel.cs`
- `VoiceSynthesisViewModel.cs`
- `TimelineViewModel.cs`
- `GlobalSearchViewModel.cs`
- `CommandPaletteViewModel.cs`

**Remediation**: Change inheritance from `ObservableObject` to `BaseViewModel`

**Estimated Effort**: 2-3 hours

**Status**: COMPLETE (2026-02-10)
- `VoiceSynthesisViewModel`: Updated to inherit `BaseViewModel`, constructor calls `base(AppServices.GetViewModelContext())`, `Dispose(bool)` override added
- `ProfilesViewModel`: Updated to inherit `BaseViewModel`, constructor calls `base(AppServices.GetViewModelContext())`
- `TimelineViewModel` (Views/Panels): Updated to inherit `BaseViewModel`, constructor calls `base(AppServices.GetViewModelContext())`
- `GlobalSearchViewModel` and `CommandPaletteViewModel`: Already correctly inherit `BaseViewModel`

---

### GAP-006: Direct HttpClient Instantiation [HIGH] ✅ COMPLETE

**Files Affected**:
- `VoiceCloningWizardViewModel.cs:354`
- `UpscalingViewModel.cs:307`
- `DeepfakeCreatorViewModel.cs:406`

**Remediation**: Inject `IHttpClientFactory`, use factory to create clients

**Estimated Effort**: 2-3 hours

**Status**: COMPLETE (2026-02-10) - Verified via grep: No direct `new HttpClient()` instantiation found in ViewModel files. All HTTP operations use `IBackendClient` service or factory-created clients.

---

## 4. Medium Priority Gaps

| ID | Gap | Remediation | Effort |
|----|-----|-------------|--------|
| GAP-007 | FastAPI in services layer | Move HTTPException to routes | 2h | ✅ COMPLETE (ServiceError + PreflightError; service_error_handler; synthesis_service, transcription_service, voice_helpers no longer import FastAPI) |
| GAP-008 | Routes import engine utilities | Move to service layer | 4h | ✅ COMPLETE (2026-03-10) — circuit_breaker_facade.py; PathService for get_models_path; rvc, health, voice, video_gen, image_gen, settings updated |
| GAP-009 | Direct WebSocket client instantiation | Inject via DI | 2h | ✅ COMPLETE (PipelineConversationView, RealTimeVoiceConverterView pass IWebSocketClientFactory from AppServices; ViewModels use factory when provided) |
| GAP-010 | Unified Error Envelope not standardized | Create ErrorEnvelope class | 4h | ✅ COMPLETE (ErrorDetail + severity + recovery_suggestion; shared/schemas/error-envelope.schema.json; ERROR_HANDLING_GUIDE updated) |
| GAP-011 | HasError not in BaseViewModel | Add computed property | 1h | ✅ COMPLETE (BaseViewModel has HasError; ABTestingViewModel/EmbeddingExplorerViewModel refactored to use base) |
| GAP-012 | Short-term memory not implemented | Add sliding window | 4h | ✅ COMPLETE (2026-03-10) — ContextAllocator memory keeps most recent items; test_context_allocator sliding window test |
| GAP-013 | WebSocket Topics not documented | Create reference doc | 2h | ✅ COMPLETE (2026-03-06) — docs/REFERENCE/WEBSOCKET_TOPICS_REFERENCE.md; WEBSOCKET_GUIDE.md updated |

---

## 5. Low Priority Gaps

| ID | Gap | Remediation | Effort |
|----|-----|-------------|--------|
| GAP-014 | UI Virtualization not universal | Audit ListView controls | 4h |
| GAP-015 | Command Palette docs incomplete | Complete documentation | 2h | ✅ COMPLETE (2026-03-10) — COMMAND_PALETTE_GUIDE.md aligned with IUnifiedCommandRegistry, CommandPaletteService, Ctrl+P |
| GAP-016 | Contract validation not enforced | Add schema validation | 8h |
| GAP-017 | Import linting rules not configured | Add pre-commit hooks | 2h |

---

## 6. Remediation Priority Matrix

| Priority | Gap IDs | Total Effort | Impact |
|----------|---------|--------------|--------|
| **P0 (Critical)** | GAP-001 | 4-6h | Documentation integrity |
| **P1 (High)** | GAP-002, GAP-003, GAP-004, GAP-005, GAP-006 | 32-48h | Architecture compliance |
| **P2 (Medium)** | GAP-007 to GAP-013 | 19h | Code quality |
| **P3 (Low)** | GAP-014 to GAP-017 | 16h | Polish |

---

## 7. Recommended Task Creation

### Task: TASK-0025 - Create Missing ADR Files [P0]

**Objective**: Create 13 placeholder ADRs with PENDING status

**Acceptance Criteria**:
- [ ] All 13 ADR files created in `docs/architecture/decisions/`
- [ ] Each ADR has Context, Decision, Consequences sections
- [ ] Each ADR marked with `Status: PENDING`
- [ ] CANONICAL_REGISTRY references valid

**Assigned Role**: System Architect (Role 1)

---

### Task: TASK-0026 - Introduce Engine Interface Layer [P1]

**Objective**: Create abstraction layer between routes and engines

**Acceptance Criteria**:
- [ ] `backend/interfaces/` directory created
- [ ] Engine interfaces defined (ITTSEngine, ISTTEngine, etc.)
- [ ] Routes refactored to use interfaces
- [ ] DI configured for engine implementations

**Assigned Role**: Core Platform (Role 4)

---

### Task: TASK-0027 - ViewModel DI Refactor [P1]

**Objective**: Replace AppServices anti-pattern with proper DI

**Acceptance Criteria**:
- [ ] DI container configured for ViewModels
- [ ] Views updated to use DI resolution
- [ ] AppServices.Get...() calls removed
- [ ] All ViewModels inherit BaseViewModel

**Assigned Role**: UI Engineer (Role 3)

---

### Task: TASK-0028 - Clean Architecture Compliance [P2]

**Objective**: Fix remaining architecture violations

**Acceptance Criteria**:
- [ ] FastAPI removed from services layer
- [ ] Route utility imports moved to services
- [ ] HttpClient and WebSocket clients injected via DI
- [ ] Business logic moved from code-behind to ViewModels

**Assigned Role**: System Architect (Role 1)

---

## 8. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| ADR creation delays | Medium | Low | Use placeholder approach |
| Engine interface refactor breaks routes | Medium | High | Incremental migration with tests |
| DI refactor causes runtime errors | Low | Medium | Comprehensive testing |
| Architecture changes introduce bugs | Medium | Medium | Incremental changes with verification |

---

## 9. Phase 7 Completion Status

- [x] All gaps consolidated from phases 3-6
- [x] Gaps categorized by severity
- [x] Remediation options documented
- [x] Effort estimates provided
- [x] Priority matrix created
- [x] Task templates created
- [x] Risk assessment complete

---

**Total Gaps**: 28
**Remediation Effort**: 71-93 hours
**Critical Path**: GAP-001 (ADR files) → GAP-002 (Engine interfaces) → GAP-003 (DI container)

---

**Next Phase**: Phase 8 - Final Audit Report & Peer Review
