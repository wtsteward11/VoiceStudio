# Slice 27 — `whisper_cpp` runtime transcript proof (operator checklist)

**Purpose:** Same-session evidence as Slice 21A: one FastAPI base URL, green preflight, non-skip `pytest -m real_whisper_cpp`, transcript JSON on disk. **ADR-056:** use explicit `engine: whisper_cpp` in the transcribe request — no silent STT substitution.

**Closure (2026-04-24):** **Tasks 175–181** recorded **runtime transcript PASS** + §8 — see [PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md](../PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md). This README remains the **canonical re-run / regression** checklist; follow **§1–§6** before any **new** PASS claim.

## Project authority vs future operator reruns (Tasks 238)

| Layer | Meaning |
| --- | --- |
| **Project authority (closed)** | **Tasks 175–181** already satisfied **runtime transcript PASS** + §8 for **`whisper_cpp`** on **`http://127.0.0.1:18293`**. [ENGINE_PARITY_MATRIX.md](../ENGINE_PARITY_MATRIX.md), [PROOF §27](../PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md), [`engine_truth_overrides.json`](../../../../tools/overseer/data/engine_truth_overrides.json), and [`engine_truth_v2.json`](../generated/engine_truth_v2.json) reflect that **PASS**. The lane is **closed** in repo truth — a failed or skipped **rerun** on another host **does not** reopen it. |
| **Future operator reruns** | To claim a **new** authoritative PASS (or to supersede **175–181**), an operator must complete **§1–§6** on **that** host, then run the **§8** mechanical checklist **on that host’s evidence**. Until then: document **blocked** / **skip** in PROOF §27 and session meta; **do not** flip matrix / overrides / verify bar / generated v2 to pretend a rerun succeeded. |

This file is the **single canonical operator checklist** for **Task 38** (runtime transcript PASS). Use the table above so a **blocked rerun** is never read as “Slice 27 reopened.”

## STT hardening regression pack (`scripts/stt_hardening_regress.ps1`)

On **success**, [`scripts/stt_hardening_regress.ps1`](../../../../scripts/stt_hardening_regress.ps1) writes **[`docs/reports/verification/generated/stt_hardening_regress_summary.json`](../generated/stt_hardening_regress_summary.json)** with UTC timestamp, exact pytest targets, exit codes, best-effort **passed** count from pytest output, and a stdout tail for audit. Use that JSON as durable evidence of the pack run (not chat). Schema checks live in **`tests/unit/scripts/test_stt_hardening_regress_summary_schema.py`** (skips if the file is missing — run the script once from repo root to generate it).

**Governance (Tasks 78–80):** Pack-wide **`passed_count`** in that JSON is **not** the same thing as **one** non-skip **`real_whisper_cpp`** transcript session (**§1–§6** below). Do not copy pack totals into STATE/PROOF/matrix prose — cite the JSON path and read **`passed_count`** / **`timestamp_utc`** there.

---

## 1. GGUF + runtime surface (readiness)

| Check | What “green” means |
| --- | --- |
| **GGUF on disk** | Satisfies [`ensure_whisper_cpp`](../../../../backend/services/model_preflight.py) / Slice 22 contract (model path discoverable per project config). |
| **Binding *or* CLI** | Either Python **`whisper_cpp`** import works **or** a CLI path satisfies the same preflight contract — see Slice 22 proof for your host outcome. |
| **Preflight boolean** | `GET {base}/api/health/preflight` → **`checks.whisper_cpp.ok: true`** (not `null`, not `false`). |

If GGUF or binding/CLI is missing, stop: record **blocked** in [PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md](../PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md); matrix stays **pending**.

### Host readiness closure (Task 93)

See **[`slice27_host_readiness_task93.md`](slice27_host_readiness_task93.md)** — one host, **green `checks.whisper_cpp`** or a **frozen** blocker tied to committed preflight / meta (pairs with **Task 91** artifacts).

### Host readiness closure (Task 101)

See **[`slice27_host_readiness_task101.md`](slice27_host_readiness_task101.md)** — repeatable operator checklist for the **current** host before **Task 102** (`pytest -m real_whisper_cpp`).

### Single-session proof authority (Tasks 135–136)

Slice 27 committed proof (**`real_whisper_cpp`**, optional **`live_whisper_cpp_preflight`**, and operator JSON under this folder) must come from **one** Uvicorn process, **one** listen port, and **one** **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** string for the whole attempt (preflight `curl`, live-preflight pytest if run, and **`real_whisper_cpp`**). Do not treat artifacts from **different** ports or **different** server lifetimes as a single session. When writing STATE/PROOF, cite **one** session’s files + pytest log tail; see [`slice27_host_readiness_task101.md`](slice27_host_readiness_task101.md) **At-a-glance**.

---

## 2. Environment variables

| Variable | Required | Example / default |
| --- | --- | --- |
| **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** | **Yes** | `http://127.0.0.1:8077` — must match the backend you start (same discipline as Slice 21A). **Slice 27 proof (Task 123):** do **not** use port **8000** on loopback unless you document a **fresh** Uvicorn from **this** repo revision on that port in the session note; the **`real_whisper_cpp`** test **`pytest.fail`**s on default **`127.0.0.1:8000`** unless **`VOICESTUDIO_SLICE27_ALLOW_DEFAULT_8000=1`**. |
| **`VOICESTUDIO_SLICE27_ALLOW_DEFAULT_8000`** | No | Set **`1`** only when **Task 123** session note documents intentional use of port **8000** for **this** revision (escape hatch — not default CI). |
| **`VOICESTUDIO_SLICE27_ARTIFACT_DIR`** | **Yes for any outcome (Tasks 91–92)** | e.g. repo `docs/reports/verification/slice27` — when set, the integration test writes **preflight capture + session meta on skip/fail**, and **`slice27_transcribe_response.json`** on **PASS**. |
| **`VOICESTUDIO_WHISPER_CPP_PROOF_WAV`** | No | Defaults to `tests/fixtures/audio/openvoice_reference_speech.wav`. |
| **`VOICESTUDIO_LIVE_PREFLIGHT_BASE_URL`** | No | **Task 125:** set to a live base URL (same session as Slice 27) to run **`pytest -m live_whisper_cpp_preflight`** — asserts **`checks.whisper_cpp`** is present with boolean **`ok`**. |

---

## 3. Backend start (Terminal A)

1. Activate the same Python env the integration test will use.
2. Set listen port (example): `$env:VOICESTUDIO_API_PORT = "8077"`.
3. Start FastAPI per project norms (e.g. `uvicorn backend.api.main:app --host 127.0.0.1 --port 8077` from repo root — adjust module path if your tree differs).
4. Confirm **`GET /health`** (or deployment equivalent) shows engines ready if your stack requires it.

**Slice 21A reference:** same pattern as Whisper transcript closure — one dedicated backend root for the whole session.

**Task 116 (control plane):** If **`pytest -m real_whisper_cpp`** fails with **`checks.whisper_cpp` missing** while **`GET /api/health/preflight`** returns **HTTP 200**, the listener is **not** current repo `health.preflight_check()` (stale build, wrong app, or proxy). Stop it and start **this** revision from repo root on your chosen port. Compare shape to [`slice27_preflight_task114.json`](slice27_preflight_task114.json).

### Task 123 — Dedicated port for Slice 27 evidence

**Slice 27 committed proof** (`real_whisper_cpp`, operator JSON under `docs/reports/verification/slice27/`) **must not** treat **`http://127.0.0.1:8000`** (or any loopback URL with port **8000**) as implicit authority unless: (1) you started **this** repo revision’s Uvicorn on that port **fresh for the session**, and (2) the session note documents that choice. Anonymous **:8000** is a common stale-listener hazard. **`pytest -m real_whisper_cpp`** **`pytest.fail`**s on that base unless **`VOICESTUDIO_SLICE27_ALLOW_DEFAULT_8000=1`** (documented exception only).

**Task 124 (live TCP):** Complement Task 114 in-process capture with a **real process** preflight — see [`slice27_preflight_task124_live.json`](slice27_preflight_task124_live.json) + [`slice27_preflight_task124_session.txt`](slice27_preflight_task124_session.txt).

---

## 4. Preflight capture (Terminal B)

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8077"
curl.exe -s "$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE/api/health/preflight" | Out-File -Encoding utf8 docs/reports/verification/slice27/slice27_preflight.json
```

Inspect JSON: **`checks.whisper_cpp.ok`** must be **`true`** before pytest.

### Task 125 — Live preflight shape guard (opt-in, Terminal B)

Requires **`VOICESTUDIO_LIVE_PREFLIGHT_BASE_URL`** (same dedicated port as **§3** / **Task 124**). CI skips when unset.

```powershell
$env:VOICESTUDIO_LIVE_PREFLIGHT_BASE_URL = "http://127.0.0.1:8077"
python -m pytest tests/integration/test_whisper_cpp_live_preflight_http.py -m live_whisper_cpp_preflight -q --no-cov
```

---

## 5. Pytest (Terminal B)

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8077"
$env:VOICESTUDIO_SLICE27_ARTIFACT_DIR = "$(Resolve-Path docs/reports/verification/slice27)"
python -m pytest tests/integration/test_transcribe_whisper_cpp_real.py -m real_whisper_cpp -q --no-cov
```

**PASS gate:** exit code **0**, summary **1 passed**, **0 skipped**.

---

## 6. Expected files (PASS and blocked/skip)

| File | Origin |
| --- | --- |
| **`slice27_preflight.json`** | Operator `curl` (recommended name/path under `slice27/`). |
| **`slice27_preflight_capture.json`** | Written by the integration test when **`VOICESTUDIO_SLICE27_ARTIFACT_DIR`** is set and **`GET /api/health/preflight`** returns **HTTP 200** (includes skips — **Tasks 91–92**). |
| **`slice27_session_meta.json`** | Same — **`schema`**: `voicestudio.slice27_session_meta.v2`; includes **`stage`**, **`blocked_reason_code`** (stable: `health_connect_failed`, `health_http_failed`, `engines_ready_false`, `preflight_not_green`, `transcribe_http_failed`), **`skip_reason`**, **`outcome`**. |
| **`slice27_transcribe_response.json`** | Written by the integration test on **PASS** only (same env var). |
| **`slice27_pytest_*.log.txt`** | Operator capture (example: **Task 91** [`slice27_pytest_real_whisper_cpp_task91.log.txt`](slice27_pytest_real_whisper_cpp_task91.log.txt)). |

Response JSON must include non-trivial **`text`** and **`"engine":"whisper_cpp"`** (or equivalent field names asserted by the test).

---

## 7. Matrix + PROOF + overrides (only after non-skip PASS)

1. Update [PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md](../PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md): status **PASS**, timestamps, commands, artifact paths, base URL (redact if needed).
2. Update [ENGINE_PARITY_MATRIX.md](../ENGINE_PARITY_MATRIX.md) **`whisper_cpp`** STT row — runtime transcript column to **PASS** with links to committed artifacts.
3. Regenerate truth: `python scripts/generate_engine_truth.py` then `python scripts/generate_engine_truth.py --schema v2` (or `.\scripts\stt_hardening_regress.ps1` after merging).
4. Update [`tools/overseer/data/engine_truth_overrides.json`](../../../../tools/overseer/data/engine_truth_overrides.json) entry **`whisper_cpp`**: `runtime_proof_status: pass`, `first_blocker: null`, `matrix_status` aligned.
5. Append **LATEST PROOF INDEX** + **Truth Sync** in [`.cursor/STATE.md`](../../../../.cursor/STATE.md).

**If pytest skipped or failed:** do **not** flip matrix to PASS; document **FAIL** or **blocked** and first blocker in PROOF §27.

---

## Preconditions (summary)

1. Backend with current code; health/preflight appropriate for your deployment.
2. **`GET /api/health/preflight`** → **`checks.whisper_cpp.ok: true`** on the **same** base URL as pytest.
3. **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** = that backend root.
4. **`VOICESTUDIO_SLICE27_ARTIFACT_DIR`** set before pytest so **`slice27_transcribe_response.json`** is written on PASS.

## Honesty

For a **new** host or regression audit: until **§1–§6** complete with **no skip**, do not claim a fresh matrix **PASS** — harness-only is not a runtime transcript PASS. The **Tasks 175–181** authority session already satisfied PASS + §8; cite that session’s artifacts unless superseded by newer proof.

---

## 8. Mechanical post-PASS flip checklist (Tasks 46–47)

**Task 138 (post-132):** **Satisfied for Tasks 175–181** — **Task 177** recorded **1 passed, 0 skipped** on **`http://127.0.0.1:18293`** with matching preflight JSON; §8 batch completed in that change-set. This section remains the **ordered** checklist for **any future** non-skip PASS that supersedes current authority.

**Historical note:** This section was **ineligible** until **Task 136** recorded **1 passed, 0 skipped** on the **same** `VOICESTUDIO_REAL_XTTS_HTTP_BASE` as the session’s live preflight JSON (**Tasks 134–135**). A **skip** with honest artifacts is **not** a matrix / overrides / verify-bar batch.

Execute **in order** only after **§1–§6** gates are green (non-skip PASS). Do not reorder or skip steps.

1. **Preflight JSON on disk** — `checks.whisper_cpp.ok == true` in the captured `slice27_preflight.json` (or equivalent path).
2. **Pytest** — `pytest -m real_whisper_cpp` shows **1 passed**, **0 skipped**, **0 failed**.
3. **Transcript artifact** — `slice27_transcribe_response.json` exists under `VOICESTUDIO_SLICE27_ARTIFACT_DIR`; non-trivial `text`; response shows **`engine`** / payload equivalent **`whisper_cpp`** (per test assertions).
4. **PROOF §27** — [PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md](../PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md): set status **PASS**, session commands, timestamps, artifact links, base URL (redact if needed).
5. **Matrix** — [ENGINE_PARITY_MATRIX.md](../ENGINE_PARITY_MATRIX.md): **`whisper_cpp`** runtime transcript column → **PASS** with links to committed artifacts (**only** after step 2).
6. **`engine_truth_overrides.json`** — entry **`whisper_cpp`**: `runtime_proof_status: pass`, `first_blocker: null`, `matrix_status` aligned with matrix prose.
7. **Regenerate engine truth** — `python scripts/generate_engine_truth.py` then `python scripts/generate_engine_truth.py --schema v2` (or `.\scripts\stt_hardening_regress.ps1` truth stage).
8. **STATE + verify bar** — [.cursor/STATE.md](../../../../.cursor/STATE.md): **LATEST PROOF INDEX**, **Truth Sync**, **Latest verify artifact**, **Last Verified Commands** reference the **same** `artifacts/verify/<stamp>/verification_report.md` as `defaults.latest_verify_artifact` in overrides; run `pytest tests/unit/scripts/test_engine_truth_verify_artifact_alignment.py -q` and confirm **PASS**.

**If any step fails:** revert partial edits; leave matrix **pending**; record **FAIL** or **blocked** in PROOF §27 with the first failing step and evidence path.
