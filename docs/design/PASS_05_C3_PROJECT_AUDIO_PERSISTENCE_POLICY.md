# Pass 05 — Slice 3 (C3) Project Audio & Persistence Policy

**Purpose:** Freeze **product and data-ownership rules** before C3 implementation. C3 is **not** a wiring-only slice; coding without this freeze risks contradictory UX and “fake saved to project” semantics.

**Date:** 2026-03-24  
**Status:** **Option B frozen** (2026-03-24) — slice 3 implements **semantics and honesty only** per §2 and §4; no new persistence APIs from record/import/transcribe paths.

**Related:** [WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md](WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md) (Pass 05 parent, §10.7 slice 3 lock), [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) Workflow 5.

**Slices 1–2 proof (closed):** Slice 1 `artifacts/verify/20260324_173141`; slice 2 `artifacts/verify/20260324_181021`.

---

## 1. Problem statement

Today, **library assets**, **project audio** (`IProjectAudioClient`), **timeline clips**, and **transcript overlay** are **different seams**. Users can experience:

- audio that exists in the library but is not project-persisted;
- transcripts and timeline overlays without a clear statement of what is “in the project”;
- synthesis-driven `SaveAudioToProjectAsync` paths that do not apply to record/import/transcribe flows.

Slice 3 **Option B** surfaces honest states in UI copy and bindable VM hints **without** new persistence calls from record/import/transcribe.

---

## 2. Policy decisions — **Slice 3 = Option B**

| Topic | Decision (Option B) |
|-------|---------------------|
| **Recorded audio ownership** | **Library-owned by default** after upload; no new `SaveAudioToProjectAsync` in slice 3. |
| **Imported audio** | **Library-owned by default**; batch vs single-file: **no new persistence bridge** in slice 3. |
| **Transcript ownership** | Transcript creation is **independent** of project-audio persistence for this slice. |
| **Timeline / `loadTranscript`** | **Overlay semantics** — does **not** imply source-audio project persistence in slice 3. |
| **User messaging** | Distinguish library-only, overlay loaded, transcript created, **not** project-persisted — explicit copy in [§8](#8-pre-implementation-map-phase-d). |
| **Minimal slice 3 rule** | **Semantics and honesty only** — **no new persistence APIs** on record/import/transcribe paths. |

---

## 3. Implementation options (reference)

- **Option A** — Add bounded `IProjectAudioClient` persistence from transcribe when project active: **deferred** (future slice; requires separate matrix).
- **Option B** — Honest status / semantics only: **chosen** for slice 3.
- **Option C** — Record-only bridge to project audio: **deferred from slice 3** (narrower than A); **execution** in separate follow-up — [WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md](WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md) — **does not** change Option B freeze below.

---

## 4. Slice 3 matrix — **C3-OptB** (single row)

| Field | Content |
|-------|---------|
| **ID** | **C3-OptB** |
| **Target** | Surface explicit persistence/state semantics (library vs overlay vs project-scoped request) via UI copy and/or VM state; **no new saves** from record/import/transcribe |
| **Primary owner** | `TranscribeViewModel` (`TranscribeAsync`, `SendSelectedTranscriptionToTimeline`); optional `TimelineViewModel` only if overlay copy is surfaced there in a follow-up |
| **Supporting** | `Resources.resw` (`Transcribe.C3.*`), `TranscribeView.xaml` (optional hint line) |
| **Tests** | `TranscribeViewModelSeamTests` (C3 cases) |
| **Proof** | `dotnet build` + seam filter (below) + `.\scripts\verify.ps1 -Quick` — **cite both** separately |

**Seam filter (same base as Pass 05 slices 1–2):**

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam"
```

---

## 5. Explicit OUT for Option B (slice 3)

- No new `SaveAudioToProjectAsync` calls from record, import, or transcribe flows.
- No batch `ImportFilesAsync` / `AssetAddedEvent` contract change.
- No timeline clip model rewrite; no shell/library redesign.
- No Option A/C persistence wiring masked as “messaging.”

---

## 6. Out of scope for slice 3 (unless explicitly re-opened)

- Shell / `PanelHost` / workspace rewrite.
- Global search / Pass 03 surfaces.
- `LibraryUseCase.ImportFilesAsync` batch event contract (separate bounded follow-up).
- `IBackendClient` extraction restart.
- Timeline data model redesign.

---

## 7. Next steps (governance)

1. Implement frozen **C3-OptB** row only; proof: seam tests + Quick verify **cited separately**.
2. Update Pass 05 §8.2 execution record and `.cursor/STATE.md` proof index after green runs.

---

## 8. Pre-implementation map (Phase D)

### 8.1 Owner methods and conditions

| Action | Method(s) | Condition | User-visible surface |
|--------|-----------|-----------|----------------------|
| After successful transcribe | `TranscribeViewModel.TranscribeAsync` | `TranscribeAudioAsync` succeeds | Resource-backed success toast + `AudioPersistenceSemanticsHint` (library vs project honesty) |
| Send transcript to Timeline | `SendSelectedTranscriptionToTimeline` | `SelectedTranscription` not null, event aggregator OK | Resource-backed success toast + hint updated (overlay-only semantics) |
| (Optional) Clear hint | `OnSelectedAudioIdChanged` or start of `TranscribeAsync` | User changes audio id / starts new transcribe | Clear or replace hint to avoid stale text |

### 8.2 Resource keys (canonical strings)

| Key | Purpose |
|-----|---------|
| `Transcribe.C3.TranscribeCompleteTitle` | Success toast title after transcribe |
| `Transcribe.C3.TranscribeCompleteDetail` | Success toast body: includes `{0}` = engine name; states library/source semantics |
| `Transcribe.C3.AudioPersistenceHint` | Bindable one-line hint (same honesty as toast body, without engine prefix if duplicated) |
| `Transcribe.C3.SendToTimelineTitle` | Success toast title |
| `Transcribe.C3.SendToTimelineDetail` | Overlay-only semantics (toast body) |
| `Transcribe.C3.SendToTimelineHint` | Bindable hint after Send to Timeline |

### 8.3 Proof template (slice 3 closure)

```text
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam"
.\scripts\verify.ps1 -Quick
```

Record seam count **separately** from Quick verify path. **Example closure:** seam **19 passed**; Quick verify `artifacts/verify/20260324_190103`.

---

## Changelog

| Date | Note |
|------|------|
| 2026-03-24 | Initial policy freeze scaffold; governance reconciliation after Pass 05 slice 2 closure. |
| 2026-03-24 | **Option B frozen**; §2 decisions; single **C3-OptB** matrix row; §5 OUT; §8 pre-code map. |
| 2026-03-24 | **C3-OptB delivered:** `TranscribeViewModel` + `Transcribe.C3.*` resources; proof `artifacts/verify/20260324_190103`. |
| 2026-03-25 | §3 **Option C** execution pointer — follow-up doc (record-only bridge); **§2 Option B** matrix unchanged. |
