# GOV-VOICESTUDIO-GAP039-REALTIME-EFFECTS-PREVIEW-BYPASS-01 — Execution Row (GAP-039)

**Status:** **Closed** (2026-04-04) — [VOICESTUDIO_GAP039_REALTIME_EFFECTS_PREVIEW_BYPASS_LANE_CLOSURE_2026-04-04.md](../reports/verification/VOICESTUDIO_GAP039_REALTIME_EFFECTS_PREVIEW_BYPASS_LANE_CLOSURE_2026-04-04.md)  
**Date:** 2026-04-04  
**Depends on:** GAP-029 export authority closed (`timeline_effect_bake.py` + `POST /api/timeline/export`); GAP-038 remains **Partial** (Win2D/GPU open per GAP-038 row).

## 1) Authority inventory (frozen)

| Concern | Owner | Canonical surface |
|--------|--------|-------------------|
| Effect chain **preview / apply** (artifact-backed process) | Backend `effects` routes + shared in-memory apply | `POST /api/effects/chains/{project_id}/{chain_id}/process` with explicit query flags; shared `backend/services/effect_chain_process.py` |
| **Chain bypass** (dry signal, no DSP) | Same route + query `bypass_chain` | Deterministic: `bypass_chain=true` → `output_audio_id` equals input `audio_id`; no new artifact |
| Per-effect on/off | Persisted chain model (`Effect.enabled`) | `PUT` chain update; ordered enabled effects only in apply path |
| **Export bake** (GAP-029) | `timeline.py` + `timeline_effect_bake.apply_timeline_export_effect_chain` | **Unchanged contract**; uses `apply_chain_model_to_audio` only — not the interactive process route |
| Client seam | `IEffectChainClient` / `EffectChainClient` (PR-11) | Single HTTP path for process; `bypassChain` + optional `preview` intent surfaced on interface |
| Effects Mixer VM | `EffectsMixerViewModel` | One private process runner; `ApplyEffectChainCommand` + `PreviewEffectChainCommand`; `IsEffectChainBypassed` drives `bypass_chain` query |

## 2) Hard IN scope

- Explicit **bypass_chain** query on project effect-chain **process** route (fail-closed validation).
- Shared in-memory processing helper used by **project** process path; legacy body `POST /api/effects/chains/{chain_id}/process` aligned where safe (see decisions doc for **strict_no_enabled** split).
- **Deterministic** responses: bypass → same `audio_id`; invalid ids → 4xx; no silent effect-free “success” where export would fail (export path unchanged).
- `IEffectChainClient.ProcessAudioWithChainAsync(..., bypassChain, preview)` + VM wiring.
- Tests: `tests/unit/backend/api/routes/test_effects.py` **non-skipped**; seam + transport tests; timeline export tests **unchanged** for GAP-029.

## 3) Hard OUT of scope

- GAP-038 GPU / Win2D waveform viewport work.
- PanelHost / shell redesign (GAP-007).
- Plugin marketplace expansion; broad DAW automation.
- Unrelated timeline edit-model / undo expansion.
- Replacing GAP-029 export route or altering export request validation except parity-safe fixes justified here (none planned).

## 4) Contract freeze (binary)

**Request — project process**

- Path: `POST /api/effects/chains/{project_id}/{chain_id}/process`
- Query: `audio_id` (required), `output_filename` (optional), `bypass_chain` (bool, default **false**), `preview` (bool, default **false** — diagnostic/parity; same processing as non-preview unless decisions doc extends).

**Bypass**

- `bypass_chain=true`: **200**, `success=true`, `output_audio_id` **must equal** request `audio_id`, message indicates chain bypass (dry).

**Export parity**

- Timeline export with `apply_effects=true` continues to use `apply_timeline_export_effect_chain` → `apply_chain_model_to_audio`; **no** use of interactive `bypass_chain` / `preview` flags on export.

## 5) Binary acceptance criteria

1. **AC1:** Shared service applies ordered **enabled** effects; bypass skips DSP deterministically.
2. **AC2:** `IEffectChainClient` sends `bypass_chain` (and `preview` when used) on the canonical URL; `BackendClientTransportPolicyTests` assert query shape.
3. **AC3:** `EffectsMixerViewModel` uses one runner for Apply + Preview; bypass toggle affects query.
4. **AC4:** `test_effects.py` runs real `TestClient` tests (no module-level skip).
5. **AC5:** `test_timeline.py` export + effect bake tests still pass (GAP-029 regression guard).
6. **AC6:** Matrix: `dotnet build`, `dotnet test` App.Tests, pytest effects + timeline routes, `pytest tests/ci -q --randomly-seed=12345`, `verify.ps1 -Quick`, `python scripts/run_verification.py` — **completion_guard** PASS at closure.

## 6) Risks and rollback

| Risk | Control |
|------|--------|
| Preview vs export semantic drift | Decisions memo + timeline tests frozen |
| Bypass ambiguity UI/backend | Single query name; VM property maps 1:1 |
| Stale governance | Closure sync: STATE, tracker, registry, proof index |

**Rollback order:** VM/XAML → `IEffectChainClient` → backend service + routes → lane docs.

## 7) Related docs

- [GOV_VOICESTUDIO_GAP039_PREVIEW_BYPASS_AUTHORITY_DECISIONS.md](GOV_VOICESTUDIO_GAP039_PREVIEW_BYPASS_AUTHORITY_DECISIONS.md)
- [GOV_VOICESTUDIO_EXPORT_AUTHORITY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_EXPORT_AUTHORITY_01_EXECUTION_ROW.md)
