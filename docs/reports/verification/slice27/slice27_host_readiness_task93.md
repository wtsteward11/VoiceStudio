# Slice 27 — Host readiness closure (Task 93)

**Purpose:** One host must resolve **why** `real_whisper_cpp` skips (or turn green). This is not a matrix claim; it is a **readiness audit** tied to [slice27/README.md](README.md) §1 and **Task 91** evidence.

## Frozen facts from Task 91 (this repo / default dev host)

| Item | Value |
| --- | --- |
| **Base URL** | `http://127.0.0.1:8000` (`VOICESTUDIO_REAL_XTTS_HTTP_BASE` default in integration test) |
| **`GET /health`** | **HTTP 200**, `engines_ready` **true** (session captured in pytest log) |
| **`GET /api/health/preflight`** | **HTTP 200** — see [`slice27_preflight_task91.json`](slice27_preflight_task91.json) (same directory as this file). |
| **`checks.whisper_cpp`** | **Absent** from preflight `checks` object — see [`slice27_session_meta.json`](slice27_session_meta.json) **`extra.checks_whisper_cpp`**. |
| **Skip reason** | Same text as **`skip_reason`** in `slice27_session_meta.json` |

## Operator checklist (green or hard red)

1. **GGUF** on disk per `ensure_whisper_cpp` / Slice 22 contract (path must match project + manifest expectations).
2. **Python binding *or* CLI** for whisper.cpp per Slice 22 proof on your machine.
3. Re-run **`GET /api/health/preflight`** on the **same** base URL as pytest — confirm either:
   - **`checks.whisper_cpp.ok: true`**, or
   - a **non-null** `checks.whisper_cpp` object with **`ok: false`** and a **`reason`** you can freeze (preferred over a missing key).
4. Only when step 3 is green: run **`pytest -m real_whisper_cpp`** with **`VOICESTUDIO_SLICE27_ARTIFACT_DIR`** set ([README §5](README.md)) and pursue **Task 102** / **Task 94** (non-skip PASS) then **Task 103** / **Task 95** (**§8**).

## Outcomes

- **Green:** Proceed to **Task 102** / **Task 94** in the same session (do not stop at “preflight 200”).
- **Red:** Append the **exact** `reason` / error string + file paths to PROOF §27 **Task 59** (do not flip matrix).

**Repeatable checklist (current host):** use **[`slice27_host_readiness_task101.md`](slice27_host_readiness_task101.md)** (Task 101).
