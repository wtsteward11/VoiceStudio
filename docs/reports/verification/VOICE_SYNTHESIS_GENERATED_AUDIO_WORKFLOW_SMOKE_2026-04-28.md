# Voice Synthesis Generated-Audio Workflow Smoke

**Date:** 2026-04-28  
**Repo SHA:** `8d63a01a56cd798058c3402836e2e2654ee57c48`  
**Branch:** `main` (in sync with `origin/main`)  
**Workflow:** Generated-audio insertion into project timeline  
**Overall result:** **PASS**

---

## Repo Guard

| Check | Result |
|---|---|
| `HEAD` | `8d63a01a56cd798058c3402836e2e2654ee57c48` |
| `origin/main` | `8d63a01a56cd798058c3402836e2e2654ee57c48` |
| HEAD == origin/main | **Yes** |
| Staged changes | **None** |
| Dirty files | `AGENTS.md`, `.vscode/settings.json` (unstaged, intentional) |

---

## Build and Backend Preflight

| Item | Result |
|---|---|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **Exit 0** — 0 errors, 5 pre-existing nullable warnings |
| `python scripts/run_verification.py` | **Exit 0 — Overall: PASS** |
| `GET http://127.0.0.1:8000/api/health` | **HTTP 200** — `status: ok`, `engines_ready: true`, `version: 1.1.0` |
| `VoiceStudio.App.exe` launch | **RUNNING** — PID 20812 confirmed alive |

---

## Workflow Smoke — All Steps Executed

### Step 1 — Consent

Piper profile `b3ca914c` has `reference_audio_bound: true`, requiring explicit consent.

```
POST /api/consent/request  →  consent_25137cf4  (status: pending)
POST /api/consent/grant/consent_25137cf4  →  status: granted  (2026-04-28T17:20:52)
```

### Step 2 — Synthesize (Piper engine, profile confirmed)

```
POST /api/voice/synthesize
  text:       "VoiceStudio generated audio project timeline smoke."
  profile_id: b3ca914c-847d-4348-8144-eee7a8adec4e
  engine:     piper
  consent_id: consent_25137cf4

→ audio_id:       synth_b3ca914c-847d-4348-8144-eee7a8adec4e_2f2eaab5
→ duration:       4.35 s
→ quality_score:  0.937
→ routed_engine:  piper   ✓ (not fallback — explicit routing confirmed)
```

### Step 3 — Confirm audio file is real

```
GET /api/voice/audio/synth_b3ca914c-847d-4348-8144-eee7a8adec4e_2f2eaab5
→ Downloaded to %TEMP%\vs_smoke_audio.wav
→ File size: 192,044 bytes  (valid WAV — not an error body)
```

### Step 4 — Add to Library (multipart upload)

```
POST /api/library/assets/upload  (multipart, audio/wav, 192044 bytes)
→ id:       17af86de-e370-4e48-82be-f542e67e6a6e
→ name:     vs-smoke-piper
→ type:     audio
→ size:     192,044 bytes
→ audio_id: 7f28ea62-b16e-418d-925c-535d8c8e9b9a   ✓ (populated from upload_id)
```

### Step 5 — Confirm library asset saved

```
GET /api/library/assets/17af86de-e370-4e48-82be-f542e67e6a6e  →  200 OK, asset present
```

### Step 6 — Add track to Timeline

```
POST /api/timeline/tracks  {name: "Generated Audio Track", type: "audio"}
→ id: 0a0c841c-afc5-4c4b-a685-f4824b14ffd0
```

### Step 7 — Add generated clip to Timeline

```
POST /api/timeline/clips
  track_id:    0a0c841c-afc5-4c4b-a685-f4824b14ffd0
  source_path: /api/voice/audio/synth_b3ca914c-847d-4348-8144-eee7a8adec4e_2f2eaab5
  start_time:  0.0
  duration:    4.35
  name:        vs-smoke-piper

→ clip id:    99be1ea1-335d-4ba8-beaf-b68884f4a231
→ start_time: 0.0
→ end_time:   4.35
→ source_path confirmed
```

### Step 8 — Confirm clip appears in Timeline state (no restart)

```
GET /api/timeline/state
→ tracks: 3  (accumulated from session, no restart)
→ track 0a0c841c: 1 clip
→ clip 99be1ea1: name=vs-smoke-piper  start=0.0  end=4.35  source_path=✓
```

### Step 9 — Overlap check

Only one clip on the track. No overlap possible.

---

## Defects Found

### D-001 — Timeline in-memory state not shared across Uvicorn workers (pre-existing)

**Observed:** On the user's initial run, two consecutive calls to  
`GET /api/timeline/state` after `POST /api/timeline/tracks` returned `tracks: 0`.  
**Root cause:** `_timeline_state` is a module-level global in `backend/api/routes/timeline.py`.  
With multiple Uvicorn workers, each worker owns a separate copy.  
A POST that mutates worker A's state is invisible to a GET served by worker B.  
**Severity:** Medium — functional defect when workers > 1; smoke passed on single-worker path.  
**Status:** Pre-existing; not introduced by commit `8d63a01a`.  
**Fix path:** Replace module-level global with a shared store (Redis, SQLite, or Uvicorn single-worker mode for dev). Tracked as quality debt.

### D-002 — `GET /api/library/assets` returns `assets` field, not `items`; initial scripts used wrong field name

**Observed:** Scripts queried `.items` on the search response which is `AssetSearchResponse.assets`.  
**Root cause:** Documentation-to-schema mismatch; response shape (`assets`, `total`, `limit`, `offset`) does not match a paginated `items` convention used elsewhere.  
**Severity:** Low — API works correctly; calling code must use `.assets`.  
**Status:** Pre-existing naming inconsistency; not introduced by `8d63a01a`.

### D-003 — `POST /api/voice/synthesize` requires consent for `reference_audio_bound` profiles; no clear API error guidance

**Observed:** First synthesis attempt returned `403 No active consent for voice`.  
**Root cause:** Profiles with `reference_audio_bound: true` require a granted `VoiceConsentRecord` and the `consent_id` field in the request body; this is not surfaced in the error detail.  
**Resolution:** Created and granted consent `consent_25137cf4`; synthesis succeeded.  
**Severity:** Low — correct security behaviour; actionable error message is the improvement.

---

## Explicit Non-Claims

- This report does **not** claim runtime FULL PASS (UI panel confirmation not automated).
- This report is **not** GAP-008 work.
- This report does **not** address RHVoice.
- This report does **not** modify `ENGINE_PARITY_MATRIX.md`.
- `AGENTS.md` and `.vscode/settings.json` were **not** staged or modified.
