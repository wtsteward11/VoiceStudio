# Generated-audio workflow durability proof (post–timeline hardening)

**Date:** 2026-04-28  
**Mission:** One bounded product proof: generated audio → library → timeline with explicit `session_id` / `revision`, reload durability, fresh-instance evidence, no spurious `TIMELINE_CONFLICT` on sequential API use.

---

## 1. Repo reality

| Item | Value |
|------|--------|
| `git rev-parse HEAD` | `781b066ece9fa3dac8ba7a88b0337680fd9a166c` |
| `git rev-parse origin/main` | `781b066ece9fa3dac8ba7a88b0337680fd9a166c` |
| Staged files (`git diff --cached --name-only`) | *(empty at proof start)* |
| Expected local dirt | `AGENTS.md`, `.vscode/settings.json`, audit docs untracked — **not staged** |

---

## 2. Build and backend preflight

| Check | Result |
|--------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **Succeeded** — 0 errors, 5 pre-existing C# nullability warnings (`AdvancedRealTimeVisualizationClient`, `TextSpeechEditorClient`, `PresetLibraryClient`, `TagManagerViewModel`) |
| `python scripts/run_verification.py` (`.venv`) | **Overall PASS** — JSON `.buildlogs/verification/last_run.json`; advisories: `runtime_proof_staleness`, `slo_baseline_freshness`, `backend_smoke_freshness` (stale only) |
| Backend | `uvicorn backend.api.main:app --host 127.0.0.1 --port 8000` with `VOICESTUDIO_TEST_MODE=stub` (`.venv` Python 3.11) |

### `GET /api/health` (after clean backend start)

- **HTTP:** 200  
- **`engines_ready`:** `true`  
- **`version_info.git_commit`:** `781b066e`  
- **SQLite / session paths in health payload:** **not in health payload** (canonical DB path from code/settings: `data/voicestudio.db` relative to process cwd, or `VOICESTUDIO_DB_PATH` if set).

Snapshot: `.buildlogs/proof_health_snapshot.json`

---

## 3. Proof method

**API-only** (`httpx` client against live uvicorn). **No WinUI operator, no heard playback.**

Per mission rules, maximum honest classification: **PARTIAL** (persistence + reload + restart + subprocess read are evidenced; not runtime FULL PASS).

---

## 4. Generated-audio evidence

| Field | Value |
|--------|--------|
| Engine / route | `POST /api/voice/synthesize` — `VOICESTUDIO_TEST_MODE=stub` routed engine **stub** (Piper attempted first; stub used) |
| `audio_id` | `synth_9f0364b0-7ead-4b55-9267-57a54796426a_23af3791` |
| `duration` | `0.24997732426303854` s |
| `audio_url` | `/api/voice/audio/synth_9f0364b0-7ead-4b55-9267-57a54796426a_23af3791` |
| On-disk file | User **Roaming** VoiceStudio artifacts path (see `.buildlogs/proof_generated_audio_durability_once.json` for exact `path`) |
| File size | **11068** bytes (**> 1 KiB**) |
| RIFF / WAV | **true** (header `RIFF`) |

---

## 5. Library and timeline evidence

| Step | HTTP | Notes |
|------|------|--------|
| Library upload | `POST /api/library/assets/upload` → **201** | Asset id `4812d64f-bbf2-4731-91a0-b3897ac4d8bf` |
| Timeline GET (before mutate) | `GET /api/timeline/state?session_id=proof-session-2026-04-28` → **200** | `revision` **0** |
| Add track | `POST /api/timeline/tracks?session_id=...` → **200** | `track_id` **`41c595e0-ea22-4099-98ad-3a1000c4f3a0`** |
| Add clip | `POST /api/timeline/clips?session_id=...` → **200** | `clip_id` **`89e3bf17-50be-4948-ba26-1ec2598a7eea`**, `start_time` **0.0**, `end_time` **0.24997732426303854** |
| Timeline GET (reload) | **200** | `revision` **2**, clip present in JSON |
| `TIMELINE_CONFLICT` / **409** | **None** on sequential flow | — |

**`session_id`:** `proof-session-2026-04-28` (non-default).

Machine-readable steps: `.buildlogs/proof_generated_audio_durability_once.json`

---

## 6. Durability reload evidence

| Check | Outcome |
|--------|---------|
| Reload after mutations (`GET` same `session_id`) | **PASS** — clip id present; `revision` **2** |
| Fresh Python process SQLite read (`load_session_timeline_raw` in subprocess) | **PASS** — `revision` 2, `track_count` 1, same `clip_ids` |
| **Uvicorn cold restart** (stop listeners on :8000, start new process), then `GET /api/timeline/state?session_id=proof-session-2026-04-28` | **PASS** — track + clip + `revision` 2 returned |

---

## 7. Defects found (and resolution)

**Defect:** `GET /api/timeline/state` could return **stale** pre-mutation JSON because **response cache middleware** cached successful GET responses and timeline POSTs did not invalidate that cache. Symptom: persisted SQLite row contained tracks/clips while HTTP GET showed empty `tracks` (false “durability FAIL”).

**Fix (minimal):**

1. `backend/api/response_cache.py` — skip caching for paths under **`/api/timeline`**.  
2. `backend/api/lifecycle.py` — use **`config.get_connection_string()`** (was invalid `config.connection_string`) so the database adapter singleton initializes with the same SQLite URI logic as migrations.  
3. **Test:** `tests/unit/backend/api/routes/test_timeline_response_cache_regression.py` — `TestClient` on full app: GET → POST track → GET must show the new track.

---

## 8. Documentation

- This report: `docs/reports/verification/GENERATED_AUDIO_WORKFLOW_DURABILITY_PROOF_2026-04-28.md`  
- Registry: `docs/governance/CANONICAL_REGISTRY.md` — addendum row added (project convention).  
- `.cursor/STATE.md` — **not modified in this commit** (minimal STATE update was attempted and reverted to avoid accidental mojibake on megabyte-scale lines; proof + registry carry the record).

---

## 9. Non-claims

- **Not** GAP-008 / **not** Slice 46 / **not** new `MainWindow*ShellBridge`  
- **Not** RHVoice / **not** `ENGINE_PARITY_MATRIX.md` edits  
- **Not** runtime **FULL PASS** (no WinUI + human + heard playback)

---

## 10. Verdict

**PARTIAL** — Full API chain + SQLite durability + uvicorn restart + subprocess read are **PASS** with artifacts; classification capped at **PARTIAL** because proof is **API-only** per mission rules.
