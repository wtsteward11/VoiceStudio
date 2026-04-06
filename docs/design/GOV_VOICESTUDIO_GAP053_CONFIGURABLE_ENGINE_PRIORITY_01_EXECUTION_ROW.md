# GOV-VOICESTUDIO-GAP053-CONFIGURABLE-ENGINE-PRIORITY-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP053_CONFIGURABLE_ENGINE_PRIORITY_01`  
**Status:** **Closed** (2026-04-06)  
**Tracker:** [GAP-053](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** runtime-affecting

## Problem statement

TTS synthesis fallback ordering is split across YAML, hardcoded lists, and synthesis-only logic, with **no user-visible control**. Operators need a **single persisted authority** for preferred fallback order that converges with the engine router and synthesis path, without redesigning manifests or health orchestration.

## Frozen architecture decisions

1. **Authority:** `EngineSettings.engine_priority_order` in the existing `settings.json` / `/api/settings` pipeline (`ISettingsService` on the client). Empty list means “defer to YAML then hardcoded defaults.”
2. **Resolution order (highest → lowest):** user `engine_priority_order` → `config/engines.config.yaml` `routing_policy.fallback_chains.tts` via `get_config().get_fallback_chain("tts")` → hardcoded TTS default `["xtts_v2", "openvoice", "piper", "espeak"]` (manifest id `espeak`, not `espeak_ng`).
3. **Single resolver:** `backend/services/engine_priority.py` exposes `resolve_engine_priority()`; `synthesis_service.py` and `EngineRouter._get_fallback_chain()` consume the same semantics (user → YAML → default).
4. **Diagnostics:** `GET /api/settings/engine-priority/effective` returns `source`, raw `order`, `available` (installed, in order), and `skipped` (in order but not installed).
5. **UI:** Minimal Settings engine section — list + move up/down + reset; **no** drag-drop, **no** new `IBackendClient` methods (use `ISettingsClient` for the effective-priority GET).

## Acceptance contract (all required)

- [x] User non-empty `engine_priority_order` overrides YAML for TTS fallback in synthesis and router.
- [x] Empty user list falls through to YAML, then to hardcoded default when YAML empty.
- [x] Invalid engine id tokens rejected at API/settings validation (`[a-z0-9_-]+`).
- [x] Effective endpoint reports correct `source` (`user` | `yaml` | `default`).
- [x] C# settings round-trip includes `enginePriorityOrder`; ViewModel move/reset/save/load covered by tests.
- [x] `IBackendClient` gains **no** engine-priority-specific methods (anti-regression test).
- [x] Full closure verification matrix + proof artifacts — [closure](../reports/verification/VOICESTUDIO_GAP053_CONFIGURABLE_ENGINE_PRIORITY_LANE_CLOSURE_2026-04-06.md).

## Allowlist (runtime + tests + governance)

See plan §8 — `backend/api/routes/settings.py`, `backend/services/engine_priority.py`, `backend/services/synthesis_service.py`, `app/core/engines/router.py`, `src/VoiceStudio.App/Core/Models/SettingsData.cs`, `SettingsViewModel.cs`, `SettingsView.xaml`, `SettingsService.cs`, `ISettingsClient` / `SettingsClient`, new unit tests, tracker, registry, STATE, this row.

## Hard OUT

- No engine manifest / capability redesign; no engine health orchestration changes; no startup path changes; no broad settings refactor; no `IBackendClient` creep; no `engines.config.yaml` structural redesign (normalize `espeak` vs `espeak_ng` only in code paths that still hardcode); no PanelHost changes; no GAP-045 / GAP-047 reopening.

## Rollback

Revert scoped commit(s). `engine_priority_order` default `[]` restores prior YAML + default behavior.

## Changelog

- **2026-04-06:** Execution row frozen; implementation started.
- **2026-04-06:** Lane closed — configurable priority + diagnostics endpoint + Settings UI; verification matrix in closure doc.
