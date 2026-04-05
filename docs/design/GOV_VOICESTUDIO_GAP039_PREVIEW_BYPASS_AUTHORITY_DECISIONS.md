# GAP-039 — Preview & Bypass Authority Decisions (Frozen)

**Lane:** GAP-039 Realtime effects preview + bypass  
**Companion row:** [GOV_VOICESTUDIO_GAP039_REALTIME_EFFECTS_PREVIEW_BYPASS_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP039_REALTIME_EFFECTS_PREVIEW_BYPASS_01_EXECUTION_ROW.md)  
**Date:** 2026-04-04

## D1 — Preview authority owner

**Decision:** **Backend** owns preview/process semantics. The Effects Mixer **must not** simulate chain DSP locally. Any “preview” that produces processed audio goes through **`POST /api/effects/chains/{project_id}/{chain_id}/process`** (artifact spine), same as apply for this lane.

**Rationale:** Single authority for ordered enabled effects + PostFX/basic fallback matches export bake mental model; avoids VM-only drift.

**Forbidden:** Client-side approximation of chain order or parameters for “preview” without the backend.

**Binary check:** Preview command path resolves to the same client method as apply (optional `preview=true` query for logging/telemetry only; processing identical unless explicitly documented otherwise).

## D2 — Bypass authority owner

**Decision:** **Explicit HTTP query `bypass_chain` (boolean).** When `true`, the server **must not** call `apply_chain_model_to_audio`; response returns the **input** `audio_id` as `output_audio_id` with a clear message.

**Per-effect bypass** remains the persisted **`Effect.enabled`** field on the chain model (not overridden by this query).

**Forbidden:** Implicit bypass from empty body, magic headers, or undocumented defaults.

**Binary check:** `bypass_chain=true` ⇒ `output_audio_id == audio_id` and no new artifact registration for processed output.

## D3 — Export parity (GAP-029)

**Decision:** **No change** to `POST /api/timeline/export` contract. Effect bake continues via `backend/services/timeline_effect_bake.py` → `apply_chain_model_to_audio`. Interactive process route flags **`bypass_chain` / `preview` do not apply** to export.

**Forbidden:** Reusing interactive bypass to skip export bake without an explicit export API change and ADR.

**Binary check:** Existing `test_timeline.py` tests for `apply_effects` + invalid chain remain green; no new silent success when export requires effects.

## D4 — Legacy body route vs project route (`strict_no_enabled`)

**Decision:** **Project-scoped** `.../chains/{project_id}/{chain_id}/process` may return **200** with unchanged `audio_id` when **no effects are enabled** and `bypass_chain=false` (dry passthrough — UX convenience). **Legacy** `POST /api/effects/chains/{chain_id}/process` with body + `project_id` query keeps **400** when no enabled effects and `bypass_chain=false` (existing `apply_chain_model_to_audio` behavior) to avoid silent contract change for older callers.

**Binary check:** Unit tests document both behaviors explicitly.

## D5 — Mixer routes

**Decision:** **No effect-chain DSP** in `mixer.py` for this lane. Mixer continues channel/preset state only; chain process stays under **`effects`** routes.
