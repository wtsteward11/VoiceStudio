# GAP-039 Realtime Effects Preview + Bypass — Lane Closure Report

**Lane:** GOV-VOICESTUDIO-GAP039-REALTIME-EFFECTS-PREVIEW-BYPASS-01  
**Date:** 2026-04-04  
**Status:** **Closed** (bounded slice per execution row)

## 1) Objective

Deliver deterministic realtime effect preview and bypass authority with single-owner backend processing, explicit query-level bypass semantics, and preserved GAP-029 export parity.

## 2) Verification Matrix

| Check | Result | Detail |
|-------|--------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** | 661 warnings (test project) |
| `dotnet test` App.Tests | **3044 passed** / 274 skipped | +5 new GAP-039 seam tests |
| `dotnet test --filter EffectsMixerViewModelSeamTests` | **12 passed** | 4 GAP-039 AC3 tests (bypass flag, preview flag, default, non-bypass) |
| `python -m pytest tests/unit/backend/api/routes/test_effects.py` | **7 passed** | AC4: real TestClient bypass, preview, legacy 400, strict_no_enabled |
| `python -m pytest tests/unit/backend/api/routes/test_timeline.py` | **35 passed** | AC5: export + effect bake regression guard (unchanged) |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217 passed** | 2 deselected |
| `.\scripts\verify.ps1 -Quick` | **PASSED** | `artifacts/verify/20260403_225532/` |
| `python scripts/run_verification.py` | **9/9 PASS** | `last_run.json` **20260403-230029** (**completion_guard** PASS) |

## 3) Acceptance Criteria Status

| AC | Description | Status |
|----|-------------|--------|
| AC1 | Shared service applies ordered enabled effects; bypass skips DSP | **PASS** — `process_chain_in_memory` in `effect_chain_process.py`; `test_process_chain_in_memory_bypass_passthrough` + `test_process_chain_in_memory_strict_raises_when_no_enabled` |
| AC2 | `IEffectChainClient` sends `bypass_chain` + `preview` query flags | **PASS** — `ProcessAudioWithChainAsync_IncludesBypassAndPreviewQuery` in `BackendClientTransportPolicyTests` |
| AC3 | `EffectsMixerViewModel` uses one runner; bypass toggle affects query | **PASS** — `ApplyEffectChain_WhenBypassed_SendsBypassFlag`, `PreviewEffectChain_SendsPreviewFlag`, `ApplyEffectChain_WhenNotBypassed_SendsBypassFalse`, `IsEffectChainBypassed_DefaultsFalse` |
| AC4 | `test_effects.py` runs real TestClient tests (no module-level skip) | **PASS** — 7 tests, no skips |
| AC5 | `test_timeline.py` export + effect bake tests still pass | **PASS** — 35 tests unchanged |
| AC6 | Full matrix: build + dotnet test + pytest effects/timeline + pytest CI + verify.ps1 -Quick + run_verification.py | **PASS** — all green |

## 4) Implementation Summary

### Backend (Phase 3)
- `backend/services/effect_chain_process.py` — shared `process_chain_in_memory` with `bypass_chain` + `strict_no_enabled` semantics
- `backend/api/routes/effects.py` — both legacy body route and project-scoped route accept `bypass_chain` and `preview` query params
- D4 split honored: project route returns 200 on no-enabled-effects passthrough; legacy route returns 400

### Client Seam (Phase 4)
- `IEffectChainClient.ProcessAudioWithChainAsync` — `bypassChain` + `preview` params (GAP-039 doc)
- `EffectChainClient.ProcessAudioWithChainAsync` — builds query string with `bypass_chain=true` / `preview=true`

### ViewModel (Phase 4)
- `EffectsMixerViewModel.IsEffectChainBypassed` — observable property driving `bypassChain` query
- `RunEffectChainProcessAsync` — single runner for Apply + Preview; `isPreview` flag maps to `preview` query
- `ApplyEffectChainCommand` + `PreviewEffectChainCommand` — both use the single runner

### Export Parity (GAP-029)
- `timeline.py` export path unchanged; `apply_timeline_export_effect_chain` uses `apply_chain_model_to_audio` directly — not interactive bypass/preview flags
- Timeline export tests in `test_timeline.py` unchanged and passing

## 5) Governance Documents

- Execution Row: [GOV_VOICESTUDIO_GAP039_REALTIME_EFFECTS_PREVIEW_BYPASS_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP039_REALTIME_EFFECTS_PREVIEW_BYPASS_01_EXECUTION_ROW.md)
- Authority Decisions: [GOV_VOICESTUDIO_GAP039_PREVIEW_BYPASS_AUTHORITY_DECISIONS.md](../../design/GOV_VOICESTUDIO_GAP039_PREVIEW_BYPASS_AUTHORITY_DECISIONS.md)

## 6) Bugs Fixed During Lane

- `test_effects.py` path traversal: 5 levels → 6 levels to resolve project root (import failure)
- `test_effects.py` monkeypatch target: `audio_registry_service.resolve_audio_path` → `AudioRegistry.get_path` (correct import path)

## 7) Honest Limits

- This lane covers the **interactive preview + bypass authority** for effect chains. It does not cover:
  - GPU waveform rendering (GAP-038 — remains Partial)
  - PanelHost redesign (GAP-007 — remains Open)
  - DAW-level VST/plugin effect hosting (GAP-068 future scope)
  - Real-time DSP streaming (out of scope; interactive = request-response)
