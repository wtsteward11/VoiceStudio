# Workflow Coherence Pass 06 — Backup / Restore → Project, Settings, Profile Recovery

**Purpose:** Bounded pass to make **restore** semantically trustworthy end-to-end: backend replaces on-disk data (profiles, projects, settings, optional models), and the **WinUI shell** reflects that truth—no stale project lists, invalid selections, or success copy that implies UI refresh that did not occur. This pass does **not** redesign backup format, cloud sync, or the extraction subsystem.

**Date:** 2026-03-26  
**Status:** **Pass 06 open** — **slices 1–5 complete** (§8; slice 5 D6 upload **`metadata.json`** validation **2026-03-26**; seam **32** unchanged; Python **`test_backup_upload_metadata`** **8** passed; **global** `verify.ps1 -Quick` **`artifacts/verify/20260326_145710`** / **`latest_pointer.json`** **`commit_hash`** **`e2819074`**). **Hard-lock** (§1, D4, §6) unchanged except D6 authorized by §5.5.

**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) (Workflow 6), [PR-14_BACKUP_RESTORE_SCOPE.md](PR-14_BACKUP_RESTORE_SCOPE.md) (transport extraction — **not** UX coherence), [POST_EXTRACTION_TRANSITION_PLAN.md](POST_EXTRACTION_TRANSITION_PLAN.md), [WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md](WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md) (prior lane; **Pass 05 Option A/C persistence is out of scope here**).

**Authoritative prior proof:** Pass 05 slice 3 closed with `artifacts/verify/20260324_190103` (see Pass 05 §8.2).

**This pass proof:** §8 — **slice 1** seam **10**; Quick `20260324_204541`. **Slice 2** seam **27**; Quick **`20260324_221954`**. **Slice 3** seam **30**; Quick **`20260324_225957`**. **Slice 4** seam **32**; Quick **`artifacts/verify/20260325_055851`**. **Slice 5** (§5.5): Python **`tests/unit/backend/api/routes/test_backup_upload_metadata.py`** **8** passed; .NET seam **32** unchanged — §7.2; **global Quick** **`artifacts/verify/20260326_145710`** (golden-loop stub path when **`VOICESTUDIO_TEST_MODE=stub`**; **`commit_hash`** **`e2819074`**). **Quick verify does not subsume seam tests.**

---

## 1. Participating components (as-is + downstream owners)

### 1.1 Transport and API (unchanged today)

| Component | Role |
|-----------|------|
| [BackupRestoreViewModel.cs](../../src/VoiceStudio.App/ViewModels/BackupRestoreViewModel.cs) | Panel VM: list/create/download/**restore**/upload/delete; **slice 1+**: publishes `BackupRestoredEvent` when aggregator wired; **slice 2**: session-complete branches on `RestoreSettings`; **slice 3**: `RestoreBusyDetail`, `CancelRestoreCommand` (HTTP cancel token), session branches for `RestoreModels` / settings+models; partial coherence if publish fails |
| [BackupRestoreView.xaml.cs](../../src/VoiceStudio.App/Views/Panels/BackupRestoreView.xaml.cs) | Hosts VM; toasts for `ErrorMessage` / `StatusMessage` |
| [IBackupRestoreClient.cs](../../src/VoiceStudio.App/Core/Services/IBackupRestoreClient.cs) | `/api/backup` client surface |
| [BackupRestoreClient.cs](../../src/VoiceStudio.App/Services/BackupRestoreClient.cs) | HTTP via `BackendClientHttpPipeline` |
| [backup.py](../../backend/api/routes/backup.py) | Restore writes to `data/profiles`, `data/projects`, `data/settings.json`, optional `models/` |

**Not primary for this pass (unless defects force a touch):** `backend/platform/config/backup_service.py` (scheduled backup — separate from panel restore).

### 1.2 Downstream owner surfaces (likely blast radius for coherence)

| Owner | Path / contract | Why restore matters | Freeze disposition |
|-------|------------------|---------------------|-------------------|
| [ProjectStore.cs](../../src/VoiceStudio.App/Services/Stores/ProjectStore.cs) | Caches / loads projects via `IProjectsClient` + optional `StateCacheService` | Disk projects change; **in-memory list + `Current`/`Selected` may be wrong** | **IN** slice 1 — invalidate or reload so lists match disk |
| [TimelineViewModel.cs](../../src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs) | `LoadProjectsAsync` → `IProjectsClient.GetProjectsAsync`; project selection; `IContextManager.SetActiveProject` | Primary UI surface for project roster + **active project** sync | **IN** slice 1 — reload + validity |
| [ProfilesViewModel.cs](../../src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs) | `IProfilesClient`, profile list, `IContextManager` | Profiles on disk replaced; **UI list stale** | **IN** slice 1 — refresh profiles after restore |
| [IContextManager](../../src/VoiceStudio.Core/Services/IContextManager.cs) | Active project (and related context) | **ActiveProjectId may point at deleted project** after restore | **IN** slice 1 — clear or rebind with explicit behavior |
| [TranscribeViewModel.cs](../../src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs) | `SelectedProjectId` sync from context / events (Pass 05) | Stale project scope after restore | **Slice 1 — indirect only:** no `TranscribeViewModel.cs` change; coherence via `ProjectChangedEvent` / `IContextManager` after timeline post-restore handling. **Direct touch** only if a repro proves a gap. |
| [RecordingViewModel.cs](../../src/VoiceStudio.App/ViewModels/RecordingViewModel.cs) | `ProjectId` / context sync | Same class of bug as transcribe if active project invalid | **Observe** slice 1 — fix if reproduces; avoid scope creep |
| [EffectsMixerViewModel.cs](../../src/VoiceStudio.App/ViewModels/EffectsMixerViewModel.cs) | `ProjectChangedEvent`, project-scoped mixer | Stale mixer state if project removed | **OUT** slice 1 — covered indirectly if `ProjectChangedEvent` / context fire correctly; **explicit later** if gaps remain |
| [SettingsViewModel.cs](../../src/VoiceStudio.App/ViewModels/SettingsViewModel.cs) | `ISettingsService` / `ISettingsClient` | `settings.json` on disk replaced; **bound UI may not reload** | **IN** slice 2 — `OnActivatedAsync` subscribes to `BackupRestoredEvent` (idempotent: no duplicate if activated twice); `OnDeactivatedAsync` disposes; when `RestoreSettings`, `LoadSettingsAsync`. Optional `IEventAggregator` ctor param for tests. **OUT** slice 1 only |

**Rule:** Slice 1 touches **projects + profiles + context + honest backup copy**. **Slice 2** adds **settings** reload. Model-restore UX (D5) remains **out** until a signed slice.

---

## 2. As-is workflow map (code-truth)

### 2.1 Create backup

1 UI: `CreateBackupAsync` builds `BackupCreateRequest` from `Include*` flags → `POST /api/backup`.  
2 Backend: Copies `data/profiles`, `data/projects`, `data/settings.json`, optionally `models/` into a temp tree; writes `metadata.json`; zips to `{backup_id}.zip`; registers manifest in `PersistentStore("backups")`.

### 2.2 Restore backup

1 UI: User selects backup; `RestoreBackupCommand` → `RestoreBackupAsync` builds `RestoreRequest` from `Restore*` flags → `POST /api/backup/{id}/restore`.  
2 Backend: Validates zip; extracts to temp; reads `metadata.json`; for each enabled restore flag **and** matching `includes_*` in metadata, **merge-overwrites** via `shutil.copytree(..., dirs_exist_ok=True)` (profiles/projects/models) or `copy2` (settings).  
3 UI (after **slices 1–2**): On HTTP success, `BackupRestoreViewModel` publishes `BackupRestoredEvent` (when `IEventAggregator` is wired) with checkbox flags ([`BackupRestoredEvent`](../../src/VoiceStudio.Core/Events/PanelEvents.cs)). **Subscribers:** `TimelineViewModel` and `ProfilesViewModel` reload per slice 1; **`SettingsViewModel` (slice 2)** registers its subscription in **`OnActivatedAsync`** and releases it in **`OnDeactivatedAsync`** so deactivate/reactivate cycles still receive restores; when `RestoreSettings`, handler calls `LoadSettingsAsync` (dispatcher-marshalled). If `PublishAsync` throws, VM sets partial-refresh `StatusMessage`; on success, session-complete copy reflects whether settings were included (`BackupRestore.RestoreSessionCompleteMessage` vs `RestoreSessionCompleteMessageSettingsRestored`). If settings reload fails, `SettingsViewModel` surfaces `ErrorMessage` while restore RPC still succeeded. If `IEventAggregator` is null (tests/isolated host), restore API still succeeds but panels do not refresh.

### 2.3 Download / upload / delete

- **Download:** `GET /api/backup/{id}/download` — FileResponse; VM uses `FileSavePicker`.  
- **Upload:** `POST /api/backup/upload` — multipart; VM uses `FileOpenPicker`.  
- **Delete:** `DELETE /api/backup/{id}` — VM removes item from `Backups` collection.

### 2.4 Stop-short / risk points

**After slices 1–3:** Projects/profiles reload when the event bus is present and publish succeeds; settings reload requires the Settings panel to have completed **`OnActivatedAsync`** at least once since construction. Restore with **models** shows in-panel busy row + optional cancel (best-effort HTTP cancel); session messaging reflects models/settings combinations. Residual risks below remain.

| Step | Behavior |
|------|----------|
| Restore succeeds but panels stay stale | **After slice 2:** unlikely when aggregator wired — still possible if `IEventAggregator` missing, publish fails (partial message), or Settings panel not **active** (no subscription until `OnActivatedAsync`) |
| Active project id invalid | **Mitigated slice 1** via timeline reload + selection clear/reconcile; edge cases if event path skipped |
| `copytree` dirs_exist_ok | Restore **merges** into existing trees; orphaned files may remain — see **D4** |
| Success copy | **Slice 1:** honest disk vs session + merge expectation; partial path when publish fails |

---

## 3. Target behavior (Pass 06 — to be achieved in implementation slices)

1. **Disk + session coherence:** After successful restore, **project, profile, and (when opted-in) settings** presentation the user sees matches on-disk/backend **without requiring restart**, **or** messaging states clearly what did not refresh (honest).  
2. **Selection validity:** If active project no longer exists post-restore, **clear or rebind** deterministically.  
3. **Scoped refresh:** Minimal reload entry points — avoid shell-wide “refresh everything” framework.  
4. **Honest failures:** API vs corrupt zip vs partial session coherence — distinct where feasible.  
5. **Testability:** Seam / unit tests on restore-triggered behavior; Quick verify separate.

---

## 4. Defect / coherence inventory

| ID | Symptom | Owner files / seam | Likely cause | Priority | Pass 06 note |
|----|---------|-------------------|--------------|----------|--------------|
| D1 | After restore, Projects / Profiles panels show **old** lists | `ProjectStore`, `TimelineViewModel`, `ProfilesViewModel`, `BackupRestoreViewModel` | No reload after restore | **High** | **IN** slice 1 |
| D2 | Active project / timeline state **invalid** after restore | `IContextManager`, `TimelineViewModel`, `TranscribeViewModel` | No context reset or refresh | **High** | **IN** slice 1 |
| D3 | Success message implies full app recovery while shell is stale | `BackupRestoreViewModel`, [Resources.resw](../../src/VoiceStudio.App/Resources/en-US/Resources.resw) (or equivalent) | Copy/reason mismatch | **Med** | **IN** slice 1 |
| D4 | Restore **merges** directories; extra files from current session can remain | [backup.py](../../backend/api/routes/backup.py) `copytree(..., dirs_exist_ok=True)` | Backend merge semantics | **Med** | **OUT** — **no** `backup.py` semantic change in Pass 06. **IN** — user-facing **expectation-setting** only (status detail, help, or secondary line: restore merges over existing data dirs; not a full replace wipe). |
| D5 | Large model restore / no progress / no cancel affordance | [`BackupRestoreViewModel`](../../src/VoiceStudio.App/ViewModels/BackupRestoreViewModel.cs), [`BackupRestoreView.xaml`](../../src/VoiceStudio.App/Views/Panels/BackupRestoreView.xaml) | UX | **Med** | **IN** slice 3 (§5.3) — **no** backend streaming/progress API |
| D6 | Upload path: **`metadata.json`** not a VoiceStudio manifest (missing keys, wrong types, no components, unknown schema) | [`backup.py`](../../backend/api/routes/backup.py) `upload_backup` | No server-side manifest validation | **Low** | **IN** slice 5 (§5.5) |

---

## 5. Bounded matrix — owner-anchored rows

Rows are **implementation-sized**. Slice 1 executes **C1a–C3a + C4** only (see §5.1).

| ID | Target behavior | Primary owner | Supporting | Tests (anticipated) | Proof |
|----|-----------------|-------------|------------|---------------------|--------|
| **C1a** | Projects list + store state refetch or cache invalidation after successful restore | `BackupRestoreViewModel` orchestrates; `ProjectStore` / `TimelineViewModel` load path | `IProjectsClient` | Extend or add tests hitting restore → reload contract (may start from `BackupRestoreViewModelSeamTests` + mocks); `TimelineViewModelTests` if logic lives there | Build + **seam/unit filter** + Quick verify (**cite both**) |
| **C1b** | Profiles list refresh after restore | `BackupRestoreViewModel`; `ProfilesViewModel` refresh hook | `IProfilesClient` | New or extended seam tests when refresh is wired | Same |
| **C2a** | Active project validity: clear or rebind `IContextManager` when current id missing after reload | `TimelineViewModel` / coordinator; possibly shell helper | `IContextManager`, `IEventAggregator` if `ProjectChangedEvent` needed | Tests for invalid-id handling | Same |
| **C2b** | Project-dependent panels: transcribe (and recording if needed) **consistent** with post-restore context | `TranscribeViewModel`, optionally `RecordingViewModel` | Context sync from Pass 05 | `TranscribeViewModelSeam` filter subset if touched | Same |
| **C3a** | Honest success / partial coherence copy: disk restore vs in-session refresh; mention merge semantics (**D4**) | `BackupRestoreViewModel` + resources | — | Assert message severity / resource keys in seam tests | Same |
| **C4** | Regression harness: no accidental broaden | — | — | `FullyQualifiedName~BackupRestoreViewModelSeam` minimum; add filters from C1a–C2b as tests land | Same |

### 5.1 Slice 1 implementation lock (first code slice — pre-code checklist)

**IN (slice 1 only):** C1a, C1b, C2a, C2b (minimal), C3a, C4.

**OUT (slice 1):** Settings panel rebind (`SettingsViewModel` / full `ISettingsService` reload); model restore progress UX (D5); backend merge behavior change (D4); Pass 05 persistence; unrelated panel refresh framework.

**Files likely to change (anticipate; final list in PR):**

- [BackupRestoreViewModel.cs](../../src/VoiceStudio.App/ViewModels/BackupRestoreViewModel.cs)
- [ProjectStore.cs](../../src/VoiceStudio.App/Services/Stores/ProjectStore.cs) (cache invalidation or explicit reload API)
- [TimelineViewModel.cs](../../src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs)
- [ProfilesViewModel.cs](../../src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs) (refresh entry point or event subscription)
- [TranscribeViewModel.cs](../../src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs) (only if C2b requires)
- [RecordingViewModel.cs](../../src/VoiceStudio.App/ViewModels/RecordingViewModel.cs) (only if reproduction mandates)
- [Resources.resw](../../src/VoiceStudio.App/Resources/en-US/Resources.resw) (restore status / detail strings)
- [BackupRestoreViewModelSeamTests.cs](../../src/VoiceStudio.App.Tests/ViewModels/BackupRestoreViewModelSeamTests.cs)
- Additional tests: `TimelineViewModelTests` or new seam file if orchestration is extracted

**Slice 1:** Executed and closed — see §8. **Slice 2:** Executed and closed — see §8 (§5.2 sign-off **2026-03-24**).

### 5.2 Slice 2 planning lock (settings-first; no code until sign-off)

**Implementation status (2026-03-24):** **Closed** — code + proof in §8. Subsections below remain the **scope contract** for audits.

**Intent:** Remove stale **settings** UI after restore when `RestoreSettings` was true, without reopening backend merge semantics (D4) or Pass 05 persistence.

**Recommended ordering:** **Settings rebind / reload** first (slice 2). **D5** (model-restore busy UX) landed as **§5.3 / slice 3** (cancel + honest copy + in-panel busy row).

**IN (candidate slice 2 — finalize before `src/`):**

- After successful restore, when the user opted to restore settings and `BackupRestoredEvent.RestoreSettings` is true, reload or rebind **in-session** settings presentation so it matches on-disk `settings.json` (primary owner: [`SettingsViewModel.cs`](../../src/VoiceStudio.App/ViewModels/SettingsViewModel.cs); orchestration may extend [`BackupRestoreViewModel.cs`](../../src/VoiceStudio.App/ViewModels/BackupRestoreViewModel.cs) or a narrow helper; contract: same event bus or explicit refresh hook already used for slice 1).
- Honest messaging if settings reload is partial or fails (reuse D4 / partial-success pattern from slice 1).

**OUT (slice 2 unless explicitly expanded in this doc):**

- D5 model-restore busy UX (**moved to** §5.3 slice 3 — do not bundle into slice 2 retroactively).
- Bundling **settings + D5** in one implementation wave without a written expand of this subsection.
- `backup.py` behavior change; ZIP format; “refresh all panels.”
- Pass 05 Option A/C persistence (separate doc).

**Tests (anticipate):** extend seam tests or add `SettingsViewModel`-scoped tests when wiring lands; record exact `dotnet test` filters in §8; cite seam vs Quick separately.

**Exit:** Product/engineering sign-off on §5.2 IN/OUT; STATE / changelog one line; then allow slice 2 `src/` edits.

**Slice 2 sign-off:** **2026-03-24** — §5.2 **IN/OUT** accepted as written (settings reload/rebind only; D5 deferred; no `backup.py` merge change; no refresh-all-panels).

### 5.3 Slice 3 planning lock — D5 model-restore busy UX (narrow)

**Intent:** Address **D5** without new backend APIs: user sees **visible progress affordance** during restore, can **request cancel** (HTTP cancellation token — best-effort; server may still finish), and **honest session-complete copy** when `RestoreModels` was checked (and combinations with settings).

**IN (slice 3 only):**

- While `RestoreBackupAsync` is in flight: **indeterminate** busy UI in [`BackupRestoreView.xaml`](../../src/VoiceStudio.App/Views/Panels/BackupRestoreView.xaml) bound to `BackupRestoreViewModel` (`ProgressRing` or equivalent + explanatory text).
- **Copy:** When `RestoreModels` is true, show **long-running** hint during restore (`RestoreBusyDetail` or equivalent resource string: models may take significant time).
- **Cancel:** Expose `CancelRestoreCommand` (or equivalent) wired to [`EnhancedAsyncRelayCommand<BackupItem>.Cancel()`](../../src/VoiceStudio.App/Utilities/EnhancedAsyncRelayCommand.cs) so in-flight `RestoreBackupAsync` passes a **canceled** `CancellationToken` into `IBackupRestoreClient.RestoreBackupAsync` (client already forwards token to HTTP).
- **Session success strings:** Branch `StatusMessage` when `RestoreModels` / combined with `RestoreSettings` (new resource keys parallel to slice 2 settings variant).

**OUT (slice 3):**

- Backend **percent** progress, SSE, or restore job polling.
- **`backup.py`** merge / wipe semantics changes.
- **ModelManager** / training panel reload beyond what slice 1 `BackupRestoredEvent` already covers; **no** new “refresh all panels.”
- Pass 05 **Option A/C** persistence.

**Files likely to change:** `BackupRestoreViewModel.cs`, `BackupRestoreView.xaml`, `Resources.resw`, `BackupRestoreViewModelSeamTests.cs`; optional `AUTOMATION_ID_REGISTRY` for new controls.

**Tests (minimum):** delayed restore ⇒ `RestoreBusyDetail` differs when `RestoreModels`; cancel during delay ⇒ `IsRestoring` false and no spurious success publish; success ⇒ correct session resource branch when models-only / settings+models (assert via stable resource fallback substrings or behavior).

**Slice 3 sign-off:** **2026-03-25** — §5.3 **IN/OUT** accepted; **lane** Pass 06 slice 3 (not Pass 05).

### 5.4 Slice 4 planning lock — D4 merge expectation (copy-only)

**Intent:** Satisfy pass-wide **§6.1** (“honest restore messaging including merge expectations (**D4** via copy, not backend)”) with **user-visible** expectation-setting that restore **merges** into existing data directories (`copytree` / `dirs_exist_ok` — see §2.2 / **D4**), **without** changing Python merge/wipe semantics.

**IN (slice 4 only — finalize before `src/`):**

- **Copy surfaces:** Status line, secondary detail, and/or help-adjacent string on [`BackupRestoreView`](../../src/VoiceStudio.App/Views/Panels/BackupRestoreView.xaml) / [`BackupRestoreViewModel`](../../src/VoiceStudio.App/ViewModels/BackupRestoreViewModel.cs) so a user initiating **restore** sees that **existing profile/project/model files may remain** alongside restored content (merge-overwrite, not full wipe).
- **Resource keys:** Add or extend keys in [`Resources.resw`](../../src/VoiceStudio.App/Resources/en-US/Resources.resw); **no** toast that implies full disk replacement if merge is true.
- **Tests:** Extend [`BackupRestoreViewModelSeamTests`](../../src/VoiceStudio.App.Tests/ViewModels/BackupRestoreViewModelSeamTests.cs) (or narrow string/assert helpers) so merge-hint visibility or resource selection is **regression-guarded** — exact asserts documented when implementation lands.

**OUT (slice 4):**

- Any change to [`backup.py`](../../backend/api/routes/backup.py) restore merge semantics.
- ZIP format, encryption, incremental backup.
- “Refresh all panels” framework; Pass 05 Option A/C persistence.
- Bundling unrelated D6 upload work without a **new** §5 row.

**Implementation file lock (after §5.4 sign-off):** Unless §8 explicitly expands scope, touch **only:** [`BackupRestoreViewModel.cs`](../../src/VoiceStudio.App/ViewModels/BackupRestoreViewModel.cs), [`BackupRestoreView.xaml`](../../src/VoiceStudio.App/Views/Panels/BackupRestoreView.xaml), [`Resources.resw`](../../src/VoiceStudio.App/Resources/en-US/Resources.resw), [`BackupRestoreViewModelSeamTests.cs`](../../src/VoiceStudio.App.Tests/ViewModels/BackupRestoreViewModelSeamTests.cs). Any extra file needs a written justification **before** merge.

**Seam proof (baseline → slice 4):** Same extended filter as §7 / §7.1 — **`30 passed`** after slice 3; **`32 passed`** after slice 4 (+2 tests). **Quick does not subsume seam.**

**§5.4 sign-off:** **2026-03-25 — Tyler (product/engineering)** — §5.4 **IN/OUT** accepted as written; **implementation file lock** (four files) unless §8 expanded; merge-expectation **copy only**; no `backup.py` / behavior / D6 / Pass 05 spillover.

### 5.5 Slice 5 planning lock — D6 upload `metadata.json` validation (backend only)

**Intent:** After ZIP integrity checks, reject uploaded archives whose **`metadata.json`** is not a **VoiceStudio backup manifest**, so the Backup panel cannot register empty or foreign ZIPs as backups. **No** restore merge semantics change; **no** WinUI changes required (`BackupRestoreViewModel` already surfaces HTTP **400** detail via `UploadBackupFailed`).

**User-visible defect addressed:** Previously, a ZIP with **`metadata.json`** missing `includes_*` booleans (or with all `false`) could upload and appear as a backup with misleading flags.

**IN (slice 5 only):**

- After loading `metadata.json` on **`POST /api/backup/upload`**, require:
  - JSON **object** (not array/primitive).
  - Presence of **`includes_profiles`**, **`includes_projects`**, **`includes_settings`**, **`includes_models`**, each **boolean**.
  - At least one **`true`** (archive must declare at least one component).
  - **`schema_version`**: absent or **`0`** (legacy) or **`1`** accepted; integer **`> 1`** → **400** “newer than this app”; other types or negative / unsupported → **400**.
- **New backups** created via **`POST /api/backup`**: include **`schema_version`: `1`** in written `metadata.json` for forward clarity.

**OUT (slice 5):**

- Changing restore or **`backup.py`** merge / **`copytree`** semantics.
- ZIP encryption, incremental backup, or new transport format.
- **`SettingsViewModel`**, **`BackupRestoreView.xaml`**, or seam count change for Pass 06 extended filter (remain **32** unless a future row authorizes).
- Pass 05 persistence; “refresh all panels.”

**Implementation file lock (after §5.5 sign-off):** [`backend/api/routes/backup.py`](../../backend/api/routes/backup.py) (validation helpers + create-metadata field); [`tests/unit/backend/api/routes/test_backup_upload_metadata.py`](../../tests/unit/backend/api/routes/test_backup_upload_metadata.py) **only**.

**Proof:**

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0** errors.  
2. **Pass 06 seam (unchanged):**  
   `FullyQualifiedName~BackupRestoreViewModelSeam|FullyQualifiedName~ApplyBackupRestoredAsync_|FullyQualifiedName~SettingsViewModelSeam` → **32 passed**.  
3. **Slice 5 Python:**  
   `python -m pytest tests/unit/backend/api/routes/test_backup_upload_metadata.py -q` → **8** passed.  
4. `.\scripts\verify.ps1 -Quick` — PASS; record artifact in §8.

**Baseline → target:** .NET seam **32** → **32**; Python upload tests **0** → **8**.

**§5.5 sign-off:** **2026-03-26 — Tyler (product/engineering)** — §5.5 **IN/OUT** and **file lock** accepted; upload validation backend-only; no WinUI scope in this slice.

---

## 6. Strict scope — IN vs OUT (pass-wide)

### 6.1 IN (Pass 06 — may span multiple implementation slices)

- Post-restore **project and profile** coherence with on-disk state (slice 1).  
- Post-restore **settings** presentation when `RestoreSettings` (slice 2).  
- **Restore busy UX + cancel + honest models copy** when `RestoreModels` is opted (slice 3 / D5).  
- **Active project** validity and dependent panel consistency (slice 1).  
- **Honest restore messaging** including merge expectations (**D4** via copy, not backend).  
- Targeted **tests + proof artifacts** per §7.  
- **Upload** `metadata.json` validation on **`POST /api/backup/upload`** (slice 5 / §5.5).

### 6.2 OUT (Pass 06)

- ZIP format, encryption, incremental backup redesign.  
- Replacing `PersistentStore("backups")` or DB-backed backup index.  
- Cloud/off-device sync.  
- Rewriting `backup.py` service boundaries or auth middleware order ([ADR-032](../architecture/decisions/ADR-032-middleware-stack.md)).  
- **Changing** restore merge/wipe semantics in Python (**D4**) — if product ever demands full wipe, that is a **new** scoped pass / ADR, not Pass 06.  
- Full app restart architecture.  
- Broad “refresh all panels” framework.  
- Pass 05 **Option A/C** persistence follow-up (separate doc).

---

## 7. Proof expectations (implementation)

**Per closure (each implementation slice):**

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — 0 errors (repo warning policy applies).  
2. **Targeted tests** — record the **exact** `dotnet test` filter used; **Pass 06 seam proof (slices 1–2):**

   - `FullyQualifiedName~BackupRestoreViewModelSeam|FullyQualifiedName~ApplyBackupRestoredAsync_|FullyQualifiedName~SettingsViewModelSeam` — **30 passed** (slice 1: **10** first two clauses only; slice 2: **27**; slice 3: **30**).
   - **Quick verify does not prove these passed** — cite seam/unit output separately.

3. `.\scripts\verify.ps1 -Quick` — PASS; `artifacts/verify/<timestamp>/verification_report.md` complete; `artifacts/verify/latest_pointer.json` aligned only per reconciliation rules.

**Planning milestones:** Initial freeze and hard-lock rows in §8 were planning-only; **slice 1–2** rows carry verify artifacts.

### 7.1 Slice 3 (D5) — proof spec (same filter family)

**Thin seam story (manual + automated):** User checks **Restore models** → **Restore** → shell shows busy row + optional cancel → `POST /api/backup/{id}/restore` runs with cancelable token → on success, existing `BackupRestoredEvent` path unchanged; **StatusMessage** reflects models (and/or settings) via resource branches.

**Exact `dotnet test` filter** (unchanged from slices 1–2 extended proof — new tests live in existing seam classes):

`FullyQualifiedName~BackupRestoreViewModelSeam|FullyQualifiedName~ApplyBackupRestoredAsync_|FullyQualifiedName~SettingsViewModelSeam`

**Seam count:** **32 passed** (after slice 4): slice 3 baseline **30** plus **2** merge-expectation hint tests (`RestoreMergeExpectationHint_*`).

**Closure bar:** §7 bullets 1–3 unchanged; **never** cite Quick as substitute for seam filter output.

### 7.2 Slice 5 (D6) — Python proof spec

**Scope:** Backend upload manifest validation only — **not** a substitute for §7.1 seam tests.

**Command:**

```text
python -m pytest tests/unit/backend/api/routes/test_backup_upload_metadata.py -q
```

**Expected:** **8** passed (see §5.5). **Quick** remains separate proof per §7 bullet 3.

---

## 8. Execution record (fill on closure)

| Phase | Status | Proof artifact | Notes |
|-------|--------|----------------|-------|
| Freeze (initial doc) | **Complete** (2026-03-24) | N/A | Planning-only |
| **Hard-lock** (§1 downstream owners, D4, §5.1 slice 1, §6) | **Complete** (2026-03-24) | N/A | Doc-only; freeze explicit |
| **Slice 1 sign-off** (§5.1 accepted — implementation authorized) | **Complete** (2026-03-24) | N/A | Product/engineering go per plan; Settings OUT slice 1; Recording observe-only |
| **Slice 1** (C1a–C3a + C4) | **Complete** (2026-03-24) | **Seam:** `dotnet test` filter **10 passed** (`FullyQualifiedName~BackupRestoreViewModelSeam|FullyQualifiedName~ApplyBackupRestoredAsync_`). **Quick:** `artifacts/verify/20260324_204541` (separate proof; Quick does not subsume seam). | `BackupRestoredEvent` + post-restore publish; `TimelineViewModel` / `ProfilesViewModel` reload + selection reconcile; `IContextManager` via `OnSelectedProjectChanged` when project cleared; resource strings `BackupRestore.RestoreSessionCompleteMessage`, `RestoreDiskOnlyPartialRefresh`; tests extended. **Files:** `PanelEvents.cs`, `BackupRestoreViewModel(.cs)`, `BackupRestoreView.xaml.cs`, `TimelineViewModel.cs`, `ProfilesViewModel.cs`, `Resources.resw`, `BackupRestoreViewModelSeamTests.cs`, `TimelineViewModelTests.cs`. **Transcribe:** validated indirectly (`TranscribeViewModel.cs` not in diff). **OUT:** `SettingsViewModel`, `backup.py` merge, `RecordingViewModel` (observe-only). |
| **Slice 2 sign-off** (§5.2 IN/OUT) | **Complete** (2026-03-24) | N/A | Authorized settings-only slice; D5 remains deferred |
| **Slice 2** (settings coherence; §5.2) | **Complete** (2026-03-24); **lifecycle hardening** re-verified Quick `20260324_221954` | **Seam:** `dotnet test` filter **27 passed** (`FullyQualifiedName~BackupRestoreViewModelSeam|FullyQualifiedName~ApplyBackupRestoredAsync_|FullyQualifiedName~SettingsViewModelSeam`). **Quick:** `artifacts/verify/20260324_221954` (authoritative slice 2 closure; supersedes `20260324_214613`; Quick does not subsume seam). | **`SettingsViewModel`:** subscribe in `OnActivatedAsync`, dispose in `OnDeactivatedAsync`, optional `IEventAggregator` for tests; `LoadSettingsAsync` when `RestoreSettings`. **`BackupRestoreViewModel`:** session-complete string branches on `RestoreSettings`. **Resources:** `RestoreSessionCompleteMessage`, `RestoreSessionCompleteMessageSettingsRestored`. **Tests:** `SettingsViewModelSeamTests` (direct + `EventAggregator` publish + shared-aggregator restore seam). **OUT:** D5, `backup.py` merge change, refresh-all-panels. |
| **Slice 3 sign-off** (§5.3 D5 IN/OUT) | **Complete** (2026-03-25) | N/A | Lane = Pass 06 slice 3; Pass 05 not in scope |
| **Slice 3** (D5 busy UX; §5.3) | **Complete** (2026-03-24 UTC run stamp **`225957`**) | **Seam:** filter §7.1 — **30 passed**. **Quick:** `artifacts/verify/20260324_225957` (Quick does not subsume seam). | Busy row (`BackupRestoreView.xaml`) + `CancelRestoreCommand` + `RestoreBusyDetail`; `ResolveRestoreSessionCompleteResourceKey`; resources for models/settings+models/cancel. **Tests:** `BackupRestoreViewModelSeamTests` (+3). **OUT:** backend progress streaming, `backup.py`, refresh-all. |
| **Slice 4 sign-off** (§5.4 D4 copy-only) | **Complete** (2026-03-25) | N/A | §5.4 sign-off recorded; implementation authorized per file lock |
| **Slice 4** (D4 merge expectation copy; §5.4) | **Complete** (2026-03-25) | **Seam:** filter §7.1 — **32 passed**. **Quick:** `artifacts/verify/20260325_055851` (Quick does not subsume seam). | **`RestoreMergeExpectationHint`** (`BackupRestoreViewModel` + `Resources.resw`); **`BackupRestoreView.xaml`** merge-expectation line (`AutomationProperties.AutomationId`=`BackupRestore_MergeExpectationHint`). **`BackupRestoreViewModelSeamTests`** (+2). **OUT:** `backup.py`, restore RPC behavior, D6, Pass 05 persistence. |
| **Slice 5 sign-off** (§5.5 D6 upload metadata) | **Complete** (2026-03-26) | N/A | §5.5 **IN/OUT** + file lock (**`backup.py`** + **`test_backup_upload_metadata.py`** only). |
| **Slice 5** (D6 `metadata.json` validation; §5.5) | **Complete** (2026-03-26) | **Seam:** §7.1 — **32 passed** (unchanged). **Python:** §7.2 — **8 passed**. **Quick:** `verify.ps1 -Quick` **`artifacts/verify/20260326_145710`** (**PASSED**); **`latest_pointer.json`** **`commit_hash`** **`e2819074fe511fa2e89663383a529100e389b32c`**. Prior attempt **`20260326_142216`** failed golden-loop **503** (no engines); resolved by **`SynthesisService`** stub artifact when **`VOICESTUDIO_TEST_MODE=stub`** (CI golden-loop only). **Build:** `dotnet build` **0** errors. | **`upload_backup`:** `_validate_upload_backup_metadata`, `schema_version` on create; partial-upload cleanup logs **`OSError`**. **Tests:** `test_backup_upload_metadata.py`. **OUT:** restore merge, WinUI, Pass 05 persistence. |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-24 | Initial scope freeze: as-is map, defects D1–D6, matrix IN/OUT, proof §7 |
| 2026-03-24 | **Hard-lock:** §1 downstream owners table; **D4** = OUT backend / IN honest copy; matrix split C1a–C4; §5.1 slice 1 file list; §6 explicit IN/OUT; §7 test surfaces; §8 hard-lock row |
| 2026-03-24 | **Slice 1 sign-off:** §5.1 IN/OUT accepted; implementation started (`BackupRestoredEvent`, post-restore coherence) |
| 2026-03-24 | **Slice 1 closed:** C1a–C4; seam filter **10 passed**; `verify.ps1 -Quick` **artifacts/verify/20260324_204541**; STATE + backlog + registry updated |
| 2026-03-24 | **Doc truth sync:** header status = Pass 06 open / slice 1 complete; §1.1 `BackupRestoreViewModel` row updated for post-slice-1 behavior; **§5.2** slice 2 planning lock (settings-first, D5 deferred) |
| 2026-03-25 | **As-is map sync:** §2.2 restore UI steps = post–slice 1 (`BackupRestoredEvent`, timeline/profiles, messaging branches); §2.4 residual risks; §1.2 Transcribe = **indirect only**; §8 slice 1 note for Transcribe |
| 2026-03-24 | **Slice 2 closed:** §5.2 sign-off; `SettingsViewModel` + `BackupRestoreViewModel` + `Resources.resw`; seam **22 passed**; Quick `artifacts/verify/20260324_214613`; §2.2/§2.4/§1.2 Settings = post–slice 2 code truth |
| 2026-03-24 | **Slice 2 lifecycle + proof:** `SettingsViewModel` backup subscription moved to `OnActivatedAsync` / `OnDeactivatedAsync`; seam **27 passed**; Quick **`artifacts/verify/20260324_221954`**; §2.2/§2.4/§1.2 Settings row aligned to activation-bound subscription |
| 2026-03-25 | **Slice 3 (D5) freeze:** §5.3 IN/OUT; §7.1 proof spec + thin seam story; §8 sign-off row; D5 matrix **IN** slice 3; changelog **authoritative slice 2** remains **`221954`** (intermediate **`214613`** superseded for slice 2 — see §8 slice 2 row) |
| 2026-03-25 | **Slice 3 closed:** D5 UX — `BackupRestoreViewModel` cancel + busy copy; `BackupRestoreView.xaml` busy row; seam **30**; Quick **`artifacts/verify/20260324_225957`**; `AUTOMATION_ID_REGISTRY` busy/cancel rows |
| 2026-03-24 | **Slice 4 planning freeze:** §5.4 D4 merge-expectation copy-only; §8 **Planned** rows; explicit next lane in STATE (no `src/` until sign-off) |
| 2026-03-25 | **Doc hygiene:** header **Date** synced; §5.4 **implementation file lock** (four files only unless §8 expands) |
| 2026-03-25 | **Slice 4 closed:** D4 merge-expectation copy — `RestoreMergeExpectationHint`; `BackupRestoreView.xaml`; **`Resources.resw`**; seam **32**; Quick **`artifacts/verify/20260325_055851`**; `AUTOMATION_ID_REGISTRY` `BackupRestore_MergeExpectationHint` |
| 2026-03-26 | **Slice 5 (§5.5 D6):** upload `metadata.json` validation in `backup.py`; create metadata **`schema_version: 1`**; pytest **`test_backup_upload_metadata`** **8**; seam **32** unchanged; §7.2 proof spec; partial-upload cleanup uses **`logger.warning`** (no silent `except`). |
| 2026-03-26 | **Slice 5 lane — global Quick closed:** `verify.ps1 -Quick` **`artifacts/verify/20260326_145710`**; **`latest_pointer.json`** → **`e2819074`**; golden-loop unblocked via **`VOICESTUDIO_TEST_MODE=stub`** synthesis stub (`SynthesisService`); STATE + backlog synced. |
