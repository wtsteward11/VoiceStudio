# GOV-VOICESTUDIO-REALTIME-METERING-01 — Execution Row (GAP-036)

**Status:** **Closed** — closure [VOICESTUDIO_REALTIME_METERING_LANE_CLOSURE_2026-03-30.md](../reports/verification/VOICESTUDIO_REALTIME_METERING_LANE_CLOSURE_2026-03-30.md); GAP-036 **Closed** in [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md); matrix recorded in closure §2.  
**Date:** 2026-03-30  
**Depends on:** Stable WebSocket stack (`IWebSocketService`, `backend/api/ws/realtime.py` topic `meters`); mixer routes that call `broadcast_meter_updates`.

## Progress (lane execution)

- **Task 1:** `MeterWebSocketClientTests` (`src/VoiceStudio.App.Tests/Services/MeterWebSocketClientTests.cs`) — `WebSocketMessage`-shaped payloads via wire deserialization (`JsonSerializerOptionsFactory.BackendApi`), field mapping, wrong topic, malformed/null-safe paths, batch child envelope; `MeterLevelUpdate` uses explicit `JsonPropertyName` only (no shadow props). `MeterWebSocketClient` parses `JsonElement` payload directly when present.
- **Task 2 — Phase B:** **Option A** — Audio Monitoring dashboard uses **`IMeterClient` + `IContextManager`**; WebSocket-first when `IMeterClient` is non-null (HTTP seed via `LoadMetersAsync`, then `ConnectAsync` + `LevelsUpdated`); realtime updates apply only when **`channel_id` equals dashboard `AudioId`** (mixer-aligned asset id) and, when the wire sends non-empty **`project_id`**, it matches **`IContextManager.ActiveProjectId`**. If `IMeterClient` is null, **500ms HTTP polling** remains. Seam tests: `AudioMonitoringDashboardViewModelSeamTests` (match / wrong channel / wrong project / empty wire `project_id`).

## 1) Authority inventory

| Concern | Pre-lane | Canonical owner (target) |
|--------|----------|---------------------------|
| Realtime meter transport | HTTP polling (`Task.Delay` loops) in Effects Mixer / Audio Monitoring | **`meters` WebSocket topic** as **primary** path; HTTP `GetAudioMeters` as **initial sync / fallback** where backend has no stream |
| Payload shape | Client DTOs expecting dB-only fields | **Normalized `peak_level` / `rms_level` (0–1)** + `project_id` + `channel_id` in `payload`, envelope `type: update` (see `realtime.broadcast_meter_updates`) |
| Client seam | `MeterWebSocketClient` unregistered; parser mismatch with backend payload | **`IMeterClient` registered in DI**; `MeterWebSocketClient` deserializes backend `payload` into `MeterLevelUpdate` (linear + ids) |
| Panel wiring | `EffectsMixerViewModel` polls `IEffectsMeterClient` only | **Realtime on:** connect `IMeterClient`, subscribe `LevelsUpdated`, apply to channel matching `channel_id` / `SelectedAudioId` / single-channel fallback |
| Audio Monitoring Dashboard | HTTP polling only | **Phase B (Option A):** `IMeterClient` + context; `AudioId` **must** equal backend `channel_id`; wire `project_id` filtered against active project when present (see §5.1) |

## 2) Frozen wire shape (canonical)

Server → client (per message; batched via `type: batch` + `messages[]`):

```json
{
  "topic": "meters",
  "type": "update",
  "payload": {
    "project_id": "<uuid>",
    "channel_id": "<id>",
    "peak_level": 0.0,
    "rms_level": 0.0
  },
  "timestamp": "<iso8601>"
}
```

## 3) Hard IN scope (this lane)

- DI: `IMeterClient` → `MeterWebSocketClient`.
- Fix client parsing so `peak_level` / `rms_level` / `project_id` / `channel_id` drive `LevelsUpdated`.
- Effects Mixer: when realtime updates enabled, **WebSocket-first** (no HTTP poll loop); one-shot HTTP load optional for initial sync.
- Tests: seam/VM tests updated for optional `IMeterClient`; backend WS tests remain source of truth for broadcast.
- Documentation: this row + registry; closure report + STATE + gap tracker when **AC** satisfied.

## 4) Hard OUT of scope

- New engine DSP, true-peak inspector UI, LUFS live loudness (export LUFS remains GAP-041).
- PanelHost / navigation redesign.
- Replacing `IWebSocketService` with a second socket implementation.

## 5) Channel identity note

Backend broadcasts **`channel_id`** from **mixer state** (`MixerChannel.id`). Timeline **`SelectedAudioId`** should match that id when the mixer's channel represents that clip; otherwise the client uses **single-channel fallback** (apply to sole channel) — documented for QA. Long-term: bind mixer channel creation to track/clip ids (follow-on).

### 5.1 Identity and Phase B (GAP-036)

**Chosen: Option A — Reuse `channel_id`.** Realtime metering on the Audio Monitoring dashboard applies **only** when:

1. **`channel_id` from the wire equals the dashboard `AudioId`** (the value is the same **mixer-aligned** identifier used for mixer channels, not an arbitrary file path or unrelated id).
2. **Project scope:** If the payload includes a non-empty **`project_id`**, it must equal **`IContextManager.ActiveProjectId`**. If `project_id` is omitted or empty on the wire, updates still apply when (1) holds (matches Effects Mixer filtering semantics).

**Option B** (extend wire with optional `audio_id`) was **not** required for product closure and would touch backend broadcasting + ADR — **out of scope** for this lane.

**Option C** (split Phase B to a new gap) was **not** taken; Phase B is **implemented** under Option A with explicit tests.

## 6) Binary acceptance

1. **AC1:** `IMeterClient` resolveable from `AppServices` / `ServiceProvider`.
2. **AC2:** `MeterWebSocketClient` raises `LevelsUpdated` for backend `payload` shape in §2.
3. **AC3:** Effects Mixer realtime mode does **not** run the 500ms HTTP poll loop when `IMeterClient` is non-null.
4. **AC4:** **`MeterWebSocketClientTests`** present and green (transport proof: `WebSocketMessage` consumer shape, not hand-wavy JSON-only tests). See `TEST_CLASSIFICATION.md` closure-grade seam.
5. **AC5:** **Audio Monitoring Phase B** complete under §5.1 Option **A** **or** execution row explicitly defers Phase B — **this lane:** Option A **implemented** with **`AudioMonitoringDashboardViewModelSeamTests`** (positive + negative id matching).
6. **AC6:** Full `dotnet test` App.Tests + `pytest tests/ci` + `verify.ps1 -Quick` + `python scripts/run_verification.py` green; **`completion_guard` PASS** at lane closure.
7. **AC7:** Closure report + Proof Index + GAP-036 **Closed** only with matrix commands recorded in the closure doc §2.

## 7) Rollback

Remove DI registration; revert `EffectsMixerViewModel` realtime branch; revert `MeterLevelUpdate` fields if needed. Keep `realtime.broadcast_meter_updates` (mixer) unchanged — server remains backward-compatible.
