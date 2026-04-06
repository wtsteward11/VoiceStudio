# GOV-VOICESTUDIO-GAP062-TORCH-VENV-RESOLUTION-AUTHORITY-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP062_TORCH_VENV_RESOLUTION_AUTHORITY_01`  
**Status:** **Closed** (2026-04-06)  
**Tracker:** [GAP-062](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** runtime-affecting (diagnostics + backend authority)

## Problem statement

Torch-backed engine families rely on **per-family venvs** (`VenvFamilyManager` + manifest `venv_family`), but there is **no single authoritative diagnostic path** that answers: which family applies, whether the venv exists, whether `torch` imports in that venv, and what version. Provisioning script families (`torch24` / `torch26` in `create_engine_venv.py`) and runtime families (`venv_core_tts` / `venv_advanced_tts`) are **parallel naming schemes** — operators need inspectable truth without conflating the two in code this lane.

## Frozen architecture decisions

1. **Authority:** `backend/services/torch_venv_resolver.py` is the single resolver for “effective torch venv status” per `VenvFamily` (torch-relevant subset only).
2. **Resolution:** For each torch-relevant family — `is_venv_created` → if false, **missing**; else subprocess probe `python -c "import torch; print(torch.__version__)"` using that family’s `python.exe` (no torch import in FastAPI worker).
3. **Per-engine API:** `resolve_torch_runtime(engine_id)` maps engine → `VenvFamily` via `ENGINE_TO_FAMILY`; unmapped → **unresolved**; mapped to a non-torch family (e.g. FAST_TTS) → **unresolved** with explicit reason in payload.
4. **Diagnostics:** `GET /api/settings/torch-venv/effective` returns `families[]` + `source`; cached 60s (`@cache_response`).
5. **Client surface:** `ISettingsClient.GetTorchVenvStatusAsync` only — **no** new `IBackendClient` methods.

## Acceptance contract (all required)

- [x] `resolve_torch_runtime(engine_id)` returns typed fields: `status`, `family`, `python_exe`, `torch_version`, `source`, and optional `detail`.
- [x] Status values: `present`, `missing`, `incompatible`, `unresolved`.
- [x] `GET /api/settings/torch-venv/effective` returns per-family status for all torch-relevant families (core TTS, advanced TTS, STT, voice conversion).
- [x] `IBackendClient` gains **no** torch-venv-specific methods (anti-regression test).
- [x] Python unit tests cover present / missing / incompatible / unresolved paths and payload shape.
- [x] C# seam tests for `SettingsClient` path + `IBackendClient` boundary test.
- [x] Full closure matrix including `verify.ps1 -Quick` — [closure](../reports/verification/VOICESTUDIO_GAP062_TORCH_VENV_RESOLUTION_AUTHORITY_LANE_CLOSURE_2026-04-06.md).

## Allowlist

`backend/services/torch_venv_resolver.py` (new), `backend/api/routes/settings.py`, `src/VoiceStudio.App/Core/Models/TorchVenvStatusResponse.cs` (new), `ISettingsClient.cs`, `SettingsClient.cs`, `scripts/engines/create_engine_venv.py` (comment block only), tests, tracker, registry, STATE, closure report, this row.

## Hard OUT

No installer redesign; no startup flow changes; no engine manifest schema redesign; no full environment bootstrap; no UI panel; no `IBackendClient` creep; no merging `torch24`/`torch26` script families with `VenvFamily` enums in this lane (documentation only).

## Rollback

Revert scoped commit(s). Endpoint removal restores prior behavior (no consumer dependency in product UI this lane).

## Changelog

- **2026-04-06:** Row frozen; implementation and closure.
- **2026-04-06:** Lane closed — resolver, `/api/settings/torch-venv/effective`, `ISettingsClient`, tests, verification matrix in closure doc.
