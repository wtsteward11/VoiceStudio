# Pass 05 — Persistence follow-up (Option C before Option A)

**Purpose:** Bounded **execution** matrix for **Option C** from [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md): **record-only** bridge from successful library upload to **`IProjectAudioClient.SaveAudioToProjectAsync`** when an active project is present.

**Date:** 2026-03-25  
**Status:** **Slice 1 (P05-Persist-C1) — complete** (2026-03-25); proof §8. **Option A** (transcribe → project persistence) remains **out of scope** until a **new** signed matrix authorizes it.

**Parent:** [WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md](WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md) (Pass 05). **Policy reference only:** [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md) — **Option B closure is frozen**; do not reopen §8 there for persistence wiring.

**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) Workflow 5.

---

## 1. Sign-off (required before `src/` edits)

| Role | Decision | Date |
|------|----------|------|
| **Product / engineering** | **Option C-first** matrix below accepted; record-only `SaveAudioToProjectAsync` after successful `UploadAudioFileAsync` is **in scope**; Option A **not** in this pass. | **2026-03-25 — Tyler** (per post–Pass 06 slice 3 lane execution) |

---

## 2. Relationship to Option B and Option A

| Option | Status in this document |
|--------|-------------------------|
| **B** | **Closed** in slice 3 — semantics/messaging only; reference policy doc. |
| **C** | **In scope** — this follow-up. |
| **A** | **Explicitly OUT** — no new `SaveAudioToProjectAsync` from transcribe/import until C is complete in §8 and a new plan row authorizes A. |

---

## 3. Bounded matrix — **P05-Persist-C1**

| Field | Content |
|-------|---------|
| **ID** | **P05-Persist-C1** |
| **Target** | After **successful** recording upload to library, if **`ProjectId`** (active project) is non-null/non-empty, call **`IProjectAudioClient.SaveAudioToProjectAsync`** with library `audioId` and optional filename from local temp path. **Import/transcribe paths unchanged.** |
| **Primary owner** | `RecordingViewModel` (`StopRecordingAsync` upload success path); helper `RecordingToProjectPersistence` (`VoiceStudio.App.Services`) |
| **Supporting** | `RecordingView.xaml.cs` (DI: `IProjectAudioClient`); `ProjectAudioClient` / backend route (existing) |
| **Failure semantics** | Save failure is **non-blocking** — library upload already succeeded; log via `IErrorLoggingService`; **no** silent swallow without log |
| **Tests** | `RecordingToProjectPersistenceTests` (unit); `RecordingViewModelSeamTests` — constructor hygiene, playback URL, **`ApplyPostLibraryUploadSuccessAsync`** (project save / no project / save throws) |
| **Proof** | `dotnet build` + seam filter (§7) + `.\scripts\verify.ps1 -Quick` — **cite both** separately |

---

## 4. IN / OUT

### 4.1 IN (slice 1)

- `RecordingViewModel` receives **`IProjectAudioClient`**; after **`UploadAudioFileAsync`** success, invoke persistence helper when `ProjectId` is set.
- Unit tests for persistence helper + seam hygiene on `RecordingViewModel` constructor + **`ApplyPostLibraryUploadSuccessAsync`** (matches post-upload path after `UploadAudioFileAsync` success).
- Documentation/registry/backlog/STATE proof lines updated after green verify.

### 4.2 OUT

- **Option A:** transcribe-selected-audio → `SaveAudioToProjectAsync`.
- **Import** / batch library workflows / `AssetAddedEvent` batch contract changes.
- **TranscribeViewModel** C3 copy changes (optional future slice; may still describe library-first for non-recorded assets).
- Pass 06 scope, `backup.py` rewrite.
- **Binding → x:Bind** in `BackupRestoreView` (separate debt).

---

## 5. Proof expectations

**Seam / targeted tests (Pass 05 lane + Option C helper):**

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam|FullyQualifiedName~RecordingToProjectPersistenceTests"
```

**Quick verify** (does **not** replace seam filter output):

```text
.\scripts\verify.ps1 -Quick
```

Record seam count **separately** from Quick artifact path. Align `artifacts/verify/latest_pointer.json` per repo reconciliation rules.

---

## 6. Risks

| Risk | Mitigation |
|------|------------|
| Double-write or confusing UX | Only **one** save after upload; no change to library upload behavior. |
| Save fails (network/backend) | Logged; user still has library asset. |
| Option A slipped in early | Reject PRs; Option A requires new §5 row + sign-off. |

---

## 7. Changelog

| Date | Note |
|------|------|
| 2026-03-25 | Initial doc; sign-off; matrix **P05-Persist-C1**; §8 Planned → execution row. |
| 2026-03-25 | **Proof hardening:** `RecordingViewModel.ApplyPostLibraryUploadSuccessAsync` extracted; three seam tests for upload-success persistence envelope; seam count **27**; authoritative Quick **`20260325_031737`** (supersedes **`20260325_030225`** for closure set after hardening). |

---

## 8. Execution record

| Slice | Status | Proof (Quick artifact) | Seam note | Notes |
|-------|--------|------------------------|-----------|-------|
| **P05-Persist-C1** | **Complete** (2026-03-25) | **`artifacts/verify/20260325_031737`** (PASS) — interim **`20260325_030225`** pre-upload-path seam | **27 passed** — filter §5 (`TranscribeViewModelSeam` \| `RecordingViewModelSeam` \| `RecordingToProjectPersistenceTests`); **Quick does not subsume** seam | Record-only `SaveAudioToProjectAsync` after upload when `ProjectId` set; `RecordingToProjectPersistence`; `RecordingViewModel.ApplyPostLibraryUploadSuccessAsync` + DI |
