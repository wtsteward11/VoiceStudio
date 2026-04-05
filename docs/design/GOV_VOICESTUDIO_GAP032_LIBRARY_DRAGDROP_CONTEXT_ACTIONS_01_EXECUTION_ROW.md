# GOV-VOICESTUDIO-GAP032-LIBRARY-DRAGDROP-CONTEXT-ACTIONS-01 — Execution row (GAP-032)

## 0. Status

- **State:** **Closed** (2026-04-02).
- **Gap:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-032** — Library drag/drop and context actions (**core 3 panels** lane: Timeline, Voice Synthesis, Voice Cloning Wizard).
- **Product posture:** **GAP-045** / **GAP-047** remain **Open** (this lane is hero-path / wiring only).
- **Closure:** [VOICESTUDIO_GAP032_LIBRARY_DRAGDROP_CONTEXT_ACTIONS_LANE_CLOSURE_2026-04-02.md](../reports/verification/VOICESTUDIO_GAP032_LIBRARY_DRAGDROP_CONTEXT_ACTIONS_LANE_CLOSURE_2026-04-02.md)
- **Deconfliction (frozen):**
  - **GAP-025 / GAP-027:** Timeline insertion remains **explicit** via `AddToTimelineEvent` only (no direct clip list mutation from Library drag).
  - **GAP-007 / GAP-008:** No PanelHost / shell navigation architecture changes.
  - **Full “all panels” library DnD:** **Out of scope** for this lane; only **core3** targets are in scope.

## 1. Objective (frozen)

Wire **Library → core3** cross-panel drag/drop using `IDragDropService` + **WinUI** `AllowDrop` / `DragOver` / `Drop`, and align **context actions** with existing events:

- **Timeline:** drop publishes `AddToTimelineEvent` (same authority as Library **Add to Timeline** command).
- **Voice Synthesis:** voice-profile library assets / profile payloads publish `ProfileSelectedEvent` with `InteractionIntent.ImmediateUse`.
- **Voice Cloning Wizard:** audio library drops with a **resolvable local file path** publish `CloneReferenceSelectedEvent`; otherwise **fail-closed** with operator feedback (no new backend download path in this lane).

Remove **dead placeholder** “Add to timeline” behavior in Library dynamic file menu that only toasts without `AddToTimelineEvent`.

## 2. Hard IN (frozen)

- `LibraryViewModel.BuildCrossPanelDragPayload` provides playback id + `FilePath` / `DurationSeconds` / `AssetType` / `LibraryAssetId` metadata for targets.
- Core3 views: `UpdateDragTarget` + `ExecuteDropAsync` on drop when `IDragDropService.IsDragging`.
- Timeline: **no** `SelectedTrack.Clips.Add` for library asset drops; use `AddToTimelineEvent`.
- MSTest: library payload + drag predicate contract; `BuildCrossPanelDragPayload` unit test.
- Verification matrix on closure: `dotnet build`, full App.Tests, `pytest tests/ci`, `verify.ps1 -Quick`, `python scripts/run_verification.py` (**completion_guard** PASS).

## 3. Hard OUT (frozen)

- No new FastAPI routes or DB migrations.
- No PanelHost / **GAP-007** shell rewrite.
- No broad “all panels” library DnD sweep.
- No reopening **GAP-025 / 026 / 027 / 028** without regression proof.

## 4. Acceptance criteria

- Library asset drag calls `StartDrag` with **rich** `DragPayload` (playback id + metadata).
- Dropping on **Timeline** when service drag is active results in **`AddToTimelineEvent`** publication (audio assets only; voice-profile assets rejected for timeline insert).
- Dropping on **Voice Synthesis** accepts **Profile** payloads and **Asset** payloads whose `AssetType` is a voice-profile kind; publishes **`ProfileSelectedEvent`**.
- Dropping on **Voice Cloning Wizard** (step 1) with **local `FilePath`** publishes **`CloneReferenceSelectedEvent`**; missing local path → warning toast + failed drop result.
- Legacy Library file-menu **Add to timeline** routes through **`AddAssetToTimelineCommand`** when the asset exists in the current list.

## 5. Proof expectations

- Closure report with matrix §2 commands and artifact paths (`verify.ps1 -Quick`, `run_verification.py` **completion_guard** PASS).
- Tracker **GAP-032** row **Closed**; `CANONICAL_REGISTRY` pointer row; `.cursor/STATE.md` ACTIVE WINDOW / proof index updated in the same pass.
- **Runtime honesty:** Proof is repo / test / gate class unless separately noted (no full WinUI manual certification claimed).
