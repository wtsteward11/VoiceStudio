# Overseer Session Handoff — 2026-03-13

**Date**: 2026-03-13  
**Session Focus**: IBackendClient migration queue — ImageSearchViewModel migration  
**Verification Status**: Build GREEN; completion_guard FAIL (uncommitted changes)  
**Artifact**: `.buildlogs/verification/last_run.json`

---

## 1. EXECUTIVE SUMMARY

### What Was Done This Session

**ImageSearchViewModel migration to IImageSearchClient** — Completed. The ViewModel now uses a feature-specific client interface instead of `IBackendClient` directly. Constructor fire-and-forget removed; initial load moved to `IPanelLifecycle.OnActivatedAsync`.

### Current State

| Aspect | Status |
|--------|--------|
| **Build** | GREEN (0 errors) |
| **ImageSearchViewModel tests** | 29 passed |
| **Verification harness** | `gate_status`, `ledger_validate`, `contract_diff`, `ibackendclient_creep`, `empty_catch_check`, `xaml_safety_check`, `ui_gap_audit` — PASS |
| **completion_guard** | FAIL — uncommitted completion markers (see §5) |
| **Uncommitted changes** | ~60 modified files, ~90 untracked files (migration wave + ImageSearch) |

### What the Next Overseer Must Do First

1. **Commit the ImageSearch migration** (and optionally the full migration wave) to satisfy completion_guard.
2. **Run `.\scripts\verify.ps1 -Quick`** — confirm GREEN before any new work.
3. **Read `.cursor/STATE.md`** — confirm phase, Next 3 Steps, proof index.
4. **Pick next migration target** — Rank 7: `TemplateLibraryViewModel` (ITemplateLibraryClient) per queue doc.

---

## 2. MAIN IDEAS AND KEY SUPPORTING DETAILS

### 2.1 IBackendClient Migration Queue — Architecture Rationale

**Purpose**: Replace direct `IBackendClient` injection in ViewModels with feature-specific client interfaces. This:

- Reduces blast radius when backend contracts change
- Enables focused unit testing (mock `IImageSearchClient` instead of full `IBackendClient`)
- Aligns with ADR-007 control/data plane boundaries
- Supports seam maturity audit and honest modularity classification

**Source of truth**: `docs/design/IBACKENDCLIENT_UNRESOLVED_QUEUE.md` — live ranked list. Do **not** use `IBACKENDCLIENT_LONGTAIL_RANKING.md` for next targets (exhausted).

**Baseline**: `.ci/ibackendclient_baseline.txt` — entries with `# MIGRATED` comment are done. Creep check: `python scripts/ci/check_ibackendclient_creep.py`.

### 2.2 ImageSearchViewModel Migration — What Changed

| Component | Change |
|-----------|--------|
| **IImageSearchClient** | New interface in `src/VoiceStudio.App/Core/Services/` — SearchAsync, GetSourcesAsync, GetCategoriesAsync, GetColorsAsync, ClearHistoryAsync |
| **ImageSearchClient** | New implementation in `src/VoiceStudio.App/Services/` — thin pass-through to IBackendClient |
| **ImageSearchModels.cs** | New models in `VoiceStudio.Core.Models` — ImageSearchRequest, ImageSearchResponse, ImageSearchResult, ImageSourceInfo |
| **ImageSearchViewModel** | Uses IImageSearchClient; implements IPanelLifecycle; OnActivatedAsync loads sources/categories/colors; RefreshAsync public; OnDeactivatedAsync no-op |
| **ImageSearchView.xaml.cs** | Uses `AppServices.GetImageSearchClient()` instead of `ServiceProvider.GetBackendClient()` |
| **ImageSearchViewModelTests** | Uses `Mock<IImageSearchClient>` instead of `Mock<IBackendClient>` |
| **ImageSearchViewModelSeamTests** | New seam-aware tests — constructor, null checks, IPanelLifecycle, OnActivatedAsync verification |
| **AppServices** | Registers `IImageSearchClient`; adds `GetImageSearchClient()` |
| **Baseline** | ImageSearchViewModel line replaced with `# ImageSearchViewModel MIGRATED to IImageSearchClient (2026-03-13)` |
| **Queue doc** | Rank 6 marked MIGRATED; Rank 7 (TemplateLibraryViewModel) recommended next |

### 2.3 IPanelLifecycle Pattern — Non-Negotiable

All migrated ViewModels that previously had constructor fire-and-forget **must** implement `IPanelLifecycle`:

- **OnActivatedAsync** — Initial load (sources, categories, etc.). Called by PanelHost when panel becomes active.
- **OnDeactivatedAsync** — Cleanup; typically `Task.CompletedTask`.
- **RefreshAsync** — Public; used by refresh commands and lifecycle. Often delegates to private `RefreshAsyncInternal`.

**ADR-047**: XamlRoot deferral — no async work from Window/Page constructors. Load from `Loaded` or `OnActivatedAsync`.

### 2.4 EffectsMixerViewModel — Deferred

**Rank 1** in the queue but **deferred** until lifecycle hardened. Risks:

- `OnSelectedProjectIdChanged`, `OnSelectedAudioIdChanged` use `ContinueWith` — no `_disposalCts`, no staleness guard
- No IDisposable
- UndoRedo actions hold `_backendClient` reference

**Recommendation**: Do not migrate EffectsMixer until lifecycle hardening (CTS ownership, disposal, staleness guard) is done.

### 2.5 Verification Gates — What Passes, What Fails

| Gate | Status | Notes |
|------|--------|-------|
| gate_status | PASS | Gates B–H healthy |
| ledger_validate | PASS | Quality Ledger valid |
| contract_diff | PASS | No schema drift |
| completion_guard | **FAIL** | Uncommitted completion markers (e.g. `[x]`, `status: complete`) in plans/STATE |
| ibackendclient_creep | PASS | Baseline aligned; no new IBackendClient consumers |
| empty_catch_check | PASS | No new empty catch blocks |
| xaml_safety_check | PASS | XAML lint OK |
| ui_gap_audit | PASS | 83 gaps (0 critical) |

**To fix completion_guard**: Commit all completion markers and proof updates. Closure protocol requires commit before close.

### 2.6 Known Debt and Exceptions

| Item | Status | Reference |
|------|--------|-----------|
| TrainingViewModel lifecycle | Fire-and-forget retained by design | `docs/design/TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md` |
| BatchProcessingViewModel | Polling/WebSocket retained; selection/filter gated | `docs/design/BATCH_PROCESSING_LIFECYCLE_PATTERNS.md` |
| VoiceSynthesisService | No retry on 5xx; single attempt | Documented in seam audit |
| SEAM_MATURITY_AUDIT | IImageSearchClient not yet added to inventory | Add after commit |

---

## 3. FILE MAP — CRITICAL PATHS

| Path | Purpose |
|------|---------|
| `.cursor/STATE.md` | Session oracle — phase, task, Next 3 Steps, proof index |
| `docs/design/IBACKENDCLIENT_UNRESOLVED_QUEUE.md` | **Pick next migration target from here** |
| `.ci/ibackendclient_baseline.txt` | Baseline; update when migrating |
| `docs/design/SEAM_MATURITY_AUDIT.md` | Seam inventory; add new clients after migration |
| `scripts/verify.ps1` | Single source of truth — must stay GREEN |
| `python scripts/run_verification.py` | Gate + ledger + completion_guard |
| `python scripts/ci/check_ibackendclient_creep.py` | Baseline alignment before/after migration |
| `src/VoiceStudio.App/Services/AppServices.cs` | DI registration; add new client + GetXxxClient() |
| `src/VoiceStudio.App/Core/Services/I*.cs` | Client interfaces |
| `src/VoiceStudio.App/Services/*Client.cs` | Client implementations |
| `src/VoiceStudio.App/Services/*Models.cs` | Request/response models (VoiceStudio.Core.Models) |

---

## 4. MIGRATION PATTERN — REPEATABLE STEPS

For each ViewModel migration (e.g. TemplateLibraryViewModel):

1. **Add interface** — `src/VoiceStudio.App/Core/Services/ITemplateLibraryClient.cs`
2. **Add models** — `src/VoiceStudio.App/Services/TemplateLibraryModels.cs` (namespace `VoiceStudio.Core.Models`)
3. **Add client** — `src/VoiceStudio.App/Services/TemplateLibraryClient.cs` (thin pass-through to IBackendClient)
4. **Register** — AppServices: `AddSingleton<ITemplateLibraryClient, TemplateLibraryClient>()` and `GetTemplateLibraryClient()`
5. **Update ViewModel** — Replace IBackendClient with ITemplateLibraryClient; implement IPanelLifecycle; move initial load to OnActivatedAsync
6. **Update View** — Use `AppServices.GetTemplateLibraryClient()` when constructing ViewModel
7. **Update tests** — Mock<ITemplateLibraryClient>; add *ViewModelSeamTests.cs
8. **Update baseline** — Replace line with `# TemplateLibraryViewModel MIGRATED to ITemplateLibraryClient (date)`
9. **Update queue doc** — Mark rank MIGRATED; update Next Migration Target
10. **Update SEAM_MATURITY_AUDIT** — Add seam to inventory

**Before starting**: Run `python scripts/ci/check_ibackendclient_creep.py`.

---

## 5. UNCOMMITTED CHANGES — WHAT NEEDS COMMIT

### ImageSearch Migration (This Session)

- `src/VoiceStudio.App/Core/Services/IImageSearchClient.cs` (new)
- `src/VoiceStudio.App/Services/ImageSearchClient.cs` (new)
- `src/VoiceStudio.App/Services/ImageSearchModels.cs` (new)
- `src/VoiceStudio.App/ViewModels/ImageSearchViewModel.cs` (modified)
- `src/VoiceStudio.App/Views/Panels/ImageSearchView.xaml.cs` (modified — uses GetImageSearchClient)
- `src/VoiceStudio.App.Tests/ViewModels/ImageSearchViewModelTests.cs` (modified)
- `src/VoiceStudio.App.Tests/ViewModels/ImageSearchViewModelSeamTests.cs` (new)
- `src/VoiceStudio.App/Services/AppServices.cs` (modified — registration)
- `.ci/ibackendclient_baseline.txt` (modified)
- `docs/design/IBACKENDCLIENT_UNRESOLVED_QUEUE.md` (modified)

### Broader Migration Wave (Prior Sessions — Untracked/Modified)

Many client interfaces, implementations, models, and seam tests from prior migration waves remain uncommitted. See `git status` for full list. Completion_guard fails due to uncommitted `[x]` or `complete` markers in plans (e.g. `NEXT_10_TASKS_PLAN_V2.md`).

**Recommended**: Commit ImageSearch migration as atomic unit first. Then decide whether to commit full wave or batch by feature.

---

## 6. CLOSURE PROTOCOL REMINDER

Before marking any task complete:

1. **Skeptical Validator** (optional): `python scripts/validator_workflow.py --task TASK-XXXX`
2. **Update STATE.md** — Last Milestone, Proof Index, Next 3 Steps
3. **Update task brief** — status Complete, checkmarks, proof artifacts
4. **Update plans** — checkmark completed items
5. **Proof Index** — add artifact path, type, status
6. **Error resolution** — all discovered errors resolved or deferred with justification
7. **Completion guard** — commit completion/proof updates; run `python scripts/run_verification.py`; confirm PASS

**No close without commit.** Completion_guard enforces this.

---

## 7. NEXT 3 STEPS (FROM STATE.MD)

1. **Migration queue** — Confirm GlobalSearch, BackupRestore, APIKeyManager use seams (not IBackendClient); pick next ranked target (Rank 7: TemplateLibraryViewModel).
2. **Baseline hygiene** — Run creep check periodically; update baseline when new consumers added.
3. **Further lifecycle cleanup** — VoiceCloningWizard, Library, Training (optional follow-through).

---

## 8. TRUTH HIERARCHY (WHEN DOCS CONFLICT)

1. **Current code** — Implementation is source of truth
2. **Current ADRs** — Decision rationale
3. **Current CI results** — verify.ps1, dotnet test, pytest
4. **.cursor/STATE.md** — Session state
5. **CLAUDE.md** — Governance prompt
6. **Conversation** — Lowest precedence

---

## 9. QUICK REFERENCE COMMANDS

```powershell
# Must run before any code change
.\scripts\verify.ps1 -Quick

# Full verification (includes completion_guard)
python scripts/run_verification.py

# Bypass completion_guard (e.g. dry-run)
python scripts/run_verification.py --skip-guard

# Creep check before/after migration
python scripts/ci/check_ibackendclient_creep.py

# Build
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# ImageSearch tests
dotnet test src/VoiceStudio.App.Tests/ -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ImageSearchViewModel"
```

---

## 10. RISKS AND MITIGATIONS

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Uncommitted changes cause merge conflicts | Medium | Commit ImageSearch migration atomically; avoid mixing with other work |
| completion_guard blocks CI | High | Commit all completion markers; closure protocol requires it |
| EffectsMixer migration attempted prematurely | Low | Queue doc explicitly defers; recommend Rank 7 (TemplateLibrary) |
| SEAM_MATURITY_AUDIT drift | Low | Add IImageSearchClient to inventory after commit |
| Baseline/queue doc desync | Low | Run creep check; update both when migrating |

---

**End of handoff.** Next Overseer: read STATE.md, run verify.ps1 -Quick, commit ImageSearch migration, then proceed with next migration target or other prioritized work.
