# Slice 27 — Host readiness closure (Task 101)

**Purpose:** One host, one hard outcome: either **`checks.whisper_cpp.ok: true`** on the same base URL used for pytest, or a **frozen** blocker with paths — no ambiguous “maybe green” posture.

Canonical operator ladder: [slice27/README.md](README.md) **§1–§6** and [PROOF §27](../PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md) **Task 59** / **Task 137** (frozen blocker when not PASS).

---

## At-a-glance — Task 134 (2026-04-24, live TCP)

| Question | Answer |
| --- | --- |
| **Which URL today?** | **`http://127.0.0.1:18082`** — one dedicated-port Uvicorn from repo root (**not** anonymous **:8000**). |
| **What env did pytest use?** | **`VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:18082`** (identical string for preflight + **`real_whisper_cpp`** — **Tasks 135–136**). |
| **Preflight evidence** | [`slice27_preflight_task134_live.json`](slice27_preflight_task134_live.json) + operator commands in [`slice27_preflight_task134_session.txt`](slice27_preflight_task134_session.txt). |
| **`checks.whisper_cpp`?** | **Present** with boolean **`ok`**. This host: **`ok: false`** — GGUF missing at path in `message` (readiness red, not absent-key / not wrong-port). |
| **`pytest -m real_whisper_cpp` (Task 136)** | **1 skipped** (`preflight_not_green`); meta + capture + log: [`slice27_session_meta.json`](slice27_session_meta.json), [`slice27_preflight_capture.json`](slice27_preflight_capture.json), [`slice27_pytest_real_whisper_cpp_task136.log.txt`](slice27_pytest_real_whisper_cpp_task136.log.txt). |

---

## Operator checklist (Task 134 session — fill from live JSON)

| Step | Result / notes |
| --- | --- |
| 1. **GGUF on disk** | From [`slice27_preflight_task134_live.json`](slice27_preflight_task134_live.json) → **`checks.whisper_cpp.message`** (expects **`whisper-medium.en.gguf`** under ProgramData / `VOICESTUDIO_MODELS_PATH` layout per Slice 22). |
| 2. **Python binding or CLI** | Uvicorn log on this host: **`whisper-cpp-python` not installed** — satisfy Slice 22 binding **or** CLI in addition to GGUF for **`ok: true`**. |
| 3. **Preflight GET (live TCP)** | **`GET http://127.0.0.1:18082/api/health/preflight`** → HTTP **200**; body matches Task **114** contract (**`checks.whisper_cpp`** object, never absent). |
| 4. **Outcome** | **Readiness red** until GGUF + binding/CLI green → then [README.md](README.md) **§5–§6**. Missing **`checks.whisper_cpp`** key on **200** ⇒ **pytest.fail** (**Task 116**) — wrong/stale listener, not skip. |

---

## Session record (Task 134 — current host authority)

| Field | Value |
| --- | --- |
| **Date (UTC)** | **2026-04-24** — capture timestamp inside [`slice27_preflight_task134_live.json`](slice27_preflight_task134_live.json). |
| **Base URL (live)** | **`http://127.0.0.1:18082`** |
| **Command log** | [`slice27_preflight_task134_session.txt`](slice27_preflight_task134_session.txt) |
| **`pytest -m real_whisper_cpp` (Task 136)** | Same base; **1 skipped** — see [`slice27_session_meta.json`](slice27_session_meta.json) |

---

## Archaeology — Task 128 / Task 124 (prior dedicated port **18079**)

Prior live TCP batch (**Tasks 123–132**): [`slice27_preflight_task124_live.json`](slice27_preflight_task124_live.json), [`slice27_preflight_task124_session.txt`](slice27_preflight_task124_session.txt), Task **129** skip on **18079**. **Do not mix** with **Task 134** evidence in new operator sessions — see [README.md](README.md) **§Single-session proof authority**.

---

## Archaeology — Task 117 (TestClient / Task 114 governance batch)

| Field | Value |
| --- | --- |
| **Date (UTC)** | **2026-04-24** — **Tasks 114–122**; JSON timestamp in [`slice27_preflight_task114.json`](slice27_preflight_task114.json). |
| **Base URL (capture)** | In-process **TestClient** (not live TCP) — [`slice27_preflight_task114_capture.txt`](slice27_preflight_task114_capture.txt). |
| **`checks.whisper_cpp` summary** | **Present** — contract shape; sample file **`ok: false`** (GGUF missing on capture machine). |

---

## Archaeology — Task 91 (stale shape)

[`slice27_preflight_task91.json`](slice27_preflight_task91.json) ends **`checks`** at **`ffmpeg`** with **no** `whisper` / `whisper_cpp` keys. That shape is **incompatible** with current [`backend/api/routes/health.py`](../../../backend/api/routes/health.py) `preflight_check()` ordering. Treat Task 91 as **stale or non-repo backend** evidence, not a model for “normal” skip metadata.
