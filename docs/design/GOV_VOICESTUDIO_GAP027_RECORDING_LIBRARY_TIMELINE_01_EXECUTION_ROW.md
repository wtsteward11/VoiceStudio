# GOV-VOICESTUDIO-GAP027-RECORDING-LIBRARY-TIMELINE-01 — Execution row (GAP-027)

## 0. Status

- **State:** **Closed** (2026-04-02).
- **Gap:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-027** — Recording → Library → Timeline discoverable path.
- **Product posture:** **GAP-045** / **GAP-047** remain **Open** (this lane is hero-path wiring only).
- **Closure:** [VOICESTUDIO_GAP027_RECORDING_LIBRARY_TIMELINE_LANE_CLOSURE_2026-04-02.md](../reports/verification/VOICESTUDIO_GAP027_RECORDING_LIBRARY_TIMELINE_LANE_CLOSURE_2026-04-02.md)
- **Deconfliction (frozen):**
  - **GAP-025:** Timeline insertion remains **explicit** via `AddToTimelineEvent` only; no auto-insert from recording completion.
  - **GAP-032:** Broader library drag/drop / context-menu expansion remains **out of scope** beyond this lane’s explicit “Add to Timeline” command.
  - **GAP-007 / GAP-008:** No PanelHost / shell navigation architecture changes.

## 1. Objective (frozen)

Deliver a **discoverable, operator-driven** path: after a recording is uploaded to the library, the Library **refreshes and focuses** the new asset when the add came from **Recording**; the operator can then **explicitly** send the **selected** playable library audio to the timeline via a **command** that publishes `AddToTimelineEvent`. Timeline continues as the **single insertion authority** (reuse GAP-025 semantics). **No** new backend routes or schema.

## 2. Hard IN (frozen)

- **Recording seam:** `AssetAddedEvent` uses canonical **`PanelIds.Recording`** as `SourcePanelId` so Library can distinguish recording-origin adds.
- **Library seam:** On `AssetAddedEvent` from Recording, after reload, **select** the matching asset when it appears in the current search result set (deterministic ID match: `Id` or `AudioId`).
- **Library operator command:** Context menu + command surface **Add to Timeline** for **audio** library assets; publishes `AddToTimelineEvent` with resolved **audio id**, **path/URL**, **duration**, and **optional** `ProfileId` (null allowed — timeline uses `IContextManager.ActiveProfileId` fallback per GAP-025).
- **Timeline seam:** Reuse `TimelineViewModel` `AddToTimelineEvent` handler and **fail-closed** rules when project/profile context is missing.
- **Duplicate handoff:** Publishing the **same** `AddToTimelineEvent` fingerprint (same `AudioId` + same computed start on the **same** target track) **must not** create a second clip (idempotent / duplicate suppression).
- **Proof:** MSTest seam coverage for recording panel id, library focus, publish handoff, duplicate suppression, and fail-closed cases where applicable.
- **Verification matrix** on closure: `dotnet build`, full App.Tests, `pytest tests/ci`, `verify.ps1 -Quick`, `python scripts/run_verification.py` (**completion_guard** PASS).

## 3. Hard OUT (frozen)

- No PanelHost / **GAP-007** shell rewrite.
- No new backend API or DB migration.
- No automatic timeline insert on recording stop / upload (only explicit Add to Timeline after Library focus).
- No scope expansion to full **GAP-032** library DnD-to-timeline product surface.

## 4. Acceptance criteria

- Recording upload success publishes `AssetAddedEvent` with `SourcePanelId == PanelIds.Recording`.
- Library panel, when subscribed, reloads assets and **selects** the new recording asset after reload (when list contains that id).
- **Add to Timeline** on a selected audio asset publishes `AddToTimelineEvent`; `TimelineViewModel` inserts subject to existing project/track/profile rules.
- Missing project or missing profile (after context fallback) **does not** corrupt timeline state (existing fail-closed behavior).
- Duplicate identical handoff to the same track/start does not add a second clip.

## 5. Proof expectations

- Closure report with matrix §2 commands and artifact paths (`verify.ps1 -Quick`, `run_verification.py` **completion_guard** PASS).
- Tracker **GAP-027** row **Closed**; `CANONICAL_REGISTRY` pointer row; `.cursor/STATE.md` ACTIVE WINDOW / proof index updated in the same pass.
- **Runtime honesty:** Proof is repo / test / gate class unless separately noted (no WinUI icon-launch certification in this lane).
