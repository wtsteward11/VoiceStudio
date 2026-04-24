# PROOF — Slice 27 — whisper.cpp runtime transcript harness (Tasks 32 / **38**)

**Status:** **PASS** — **Tasks 175–181 (2026-04-24):** dedicated Uvicorn **`127.0.0.1:18293`** (`/health` **200** before pytest); green **`checks.whisper_cpp.ok`**; same-session **`pytest -m real_whisper_cpp`** **1 passed, 0 skipped**; artifacts under [`slice27/session_20260424_175_181_18293/slice27_transcribe_response.json`](slice27/session_20260424_175_181_18293/slice27_transcribe_response.json) (session folder) + **§8** mechanical batch (this change-set).

**Date:** 2026-04-23 (harness); **2026-04-24** runtime transcript authority (**Tasks 175–181**; prior **Tasks 143–148** on **18085** retained as archaeology below); checklist: [`slice27/README.md`](slice27/README.md).

## Current truth (operator contract — primary closure satisfied)

**Status:** **PASS** — **Tasks 175–181** closed the primary runtime transcript seam (**`127.0.0.1:18293`**, **`real_whisper_cpp` 1/0**, §8). The bullets below are the **ongoing contract** for **re-proof**, regression audits, or new hosts — not an open “do this to close Slice 27” gate.

Authority for any **new** live session: **one** dedicated-port Uvicorn + **one** `VOICESTUDIO_REAL_XTTS_HTTP_BASE` + live preflight JSON + same-session **`pytest -m real_whisper_cpp`** ([slice27/README.md](slice27/README.md), [Task 101 At-a-glance](slice27/slice27_host_readiness_task101.md)).

- **Contract** — In-repo **TestClient** capture proves **`checks.whisper_cpp`** is always an object with boolean **`ok`**: [`slice27_preflight_task114.json`](slice27/slice27_preflight_task114.json). Live TCP on a **non–:8000** port matches that shape (e.g. [`slice27_preflight_task134_live.json`](slice27/slice27_preflight_task134_live.json)).
- **Wrong listener** — HTTP **200** preflight **without** the **`whisper_cpp`** key ⇒ **`pytest.fail`** in **`real_whisper_cpp`** (not a skip) — wrong/stale backend vs current `preflight_check()`.
- **Dedicated port** — Anonymous **`127.0.0.1:8000`** is not implicit authority; escape hatch **`VOICESTUDIO_SLICE27_ALLOW_DEFAULT_8000=1`** only with a documented session note ([slice27/README.md](slice27/README.md) **§3**).
- **Readiness vs contract** — **`ok: false`** (GGUF / binding / CLI per `ensure_whisper_cpp`) is **readiness red** on a **valid** repo backend; missing key on **200** is **contract violation**.
- **Closure** — **Done (2026-04-24):** matrix **`whisper_cpp`** runtime transcript **PASS** + README **§8** — latest live authority **Tasks 175–181** on **`http://127.0.0.1:18293`** (session-scoped artifacts); prior **Tasks 143–148** session on **18085** remains cited in matrix archaeology.

## Task ID map (archived aliases)

| Operator runtime (`real_whisper_cpp`) | Post-PASS mechanical (README §8) | Same seam |
| --- | --- | --- |
| **Task 68 / 74 / 81 / 88 / 94 / 102 / 110** | **Task 69 / 75 / 82 / 89 / 95 / 103 / 111** | One closure: [slice27_host_readiness_task101.md](slice27/slice27_host_readiness_task101.md) + [slice27/README.md](slice27/README.md) **§1–§6**, then **§8** only after non-skip **PASS**. |

## Scope

| In scope | Out of scope |
| --- | --- |
| Opt-in integration test + `real_whisper_cpp` marker | Claiming matrix **PASS** without a real session |
| Same base URL as Slice 21A (`VOICESTUDIO_REAL_XTTS_HTTP_BASE`) | C# UI / WinUI STT |
| Preflight gate `checks.whisper_cpp.ok` | Automatic CI (no GGUF in default CI) |
| Optional JSON artifact when `VOICESTUDIO_SLICE27_ARTIFACT_DIR` is set | Full `verify.ps1` (Quick skips many stages — see PROOF §24) |

## Code / config touched (harness)

- [`tests/integration/test_transcribe_whisper_cpp_real.py`](../../../tests/integration/test_transcribe_whisper_cpp_real.py) — live flow: `/health` → preflight → library upload → `POST /api/transcribe/` with `engine: whisper_cpp`; **Task 123** — **`pytest.fail`** if loopback port **8000** without **`VOICESTUDIO_SLICE27_ALLOW_DEFAULT_8000=1`**; **[`record_slice27_skip_and_exit`](../../../tests/integration/slice27_whisper_cpp_evidence.py)** + **[`write_slice27_pass_bundle`](../../../tests/integration/slice27_whisper_cpp_evidence.py)** when **`VOICESTUDIO_SLICE27_ARTIFACT_DIR`** is set (blocked vs PASS).
- [`tests/integration/test_whisper_cpp_live_preflight_http.py`](../../../tests/integration/test_whisper_cpp_live_preflight_http.py) — **Task 125** opt-in **`live_whisper_cpp_preflight`** TCP **`GET /api/health/preflight`** shape guard (**`VOICESTUDIO_LIVE_PREFLIGHT_BASE_URL`**).
- [`pytest.ini`](../../../pytest.ini) — markers `real_whisper_cpp`, `live_whisper_cpp_preflight`; excluded from default `addopts` `-m` (opt-in).
- Operator notes: [`slice27/README.md`](slice27/README.md).

## Operator runbook (exact discipline)

See **[`slice27/README.md`](slice27/README.md)** for ordered commands: start **one** backend → set **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** → optional **`VOICESTUDIO_SLICE27_ARTIFACT_DIR`** → capture preflight JSON → **`python -m pytest tests/integration/test_transcribe_whisper_cpp_real.py -m real_whisper_cpp -q`**.

**Success criteria:** pytest **1 passed**, **0 skipped**; **`slice27_transcribe_response.json`** present under artifact dir; preflight JSON shows **`checks.whisper_cpp.ok: true`**; response JSON includes non-trivial **`text`** and **`engine":"whisper_cpp"`**.

## Verification commands (maintainer / local)

| Step | Command | Expected |
| --- | --- | --- |
| Collect only | `python -m pytest tests/integration/test_transcribe_whisper_cpp_real.py -m real_whisper_cpp --collect-only -q` | 1 test collected |
| Default CI (excludes marker) | `python -m pytest tests/unit/core/engines/test_router_stt_policy.py -q` | green (STT policy unchanged) |

**Note:** `.\scripts\verify.ps1 -Quick` does **not** run `real_whisper_cpp`; it is a **regression guard**, not deep runtime certification.

## Artifacts (PASS vs blocked — repo truth)

**Runtime transcript PASS (current authority — Tasks 175–181):** [`slice27_preflight_task175_176_18293.json`](slice27/session_20260424_175_181_18293/slice27_preflight_task175_176_18293.json) (`checks.whisper_cpp.ok: true` on **`127.0.0.1:18293`**), [`slice27_transcribe_response.json`](slice27/session_20260424_175_181_18293/slice27_transcribe_response.json), [`slice27_pytest_real_whisper_cpp_task177.log.txt`](slice27/session_20260424_175_181_18293/slice27_pytest_real_whisper_cpp_task177.log.txt), [`slice27_live_session_manifest.json`](slice27/session_20260424_175_181_18293/slice27_live_session_manifest.json), root mirror [`slice27_live_session_manifest.json`](slice27/slice27_live_session_manifest.json). See [slice27/README.md](slice27/README.md) **§6**.

**Prior PASS archaeology (Tasks 143–148 — 18085):** [`slice27_preflight_task143_green.json`](slice27/slice27_preflight_task143_green.json), [`slice27_transcribe_response.json`](slice27/slice27_transcribe_response.json) (root copy may lag session authority — prefer **session_20260424_175_181_18293** paths above), [`slice27_pytest_real_whisper_cpp_task144.log.txt`](slice27/slice27_pytest_real_whisper_cpp_task144.log.txt).

**Blocked-session evidence (archived — Task 88 / Task 91 + Tasks 99–100 refresh):** canonical skip is auditable without chat — [`slice27_preflight_task91.json`](slice27/slice27_preflight_task91.json) (full **`GET /api/health/preflight`** body), [`slice27_session_meta.json`](slice27/slice27_session_meta.json) (**`schema`**: `voicestudio.slice27_session_meta.v2`; **`blocked_reason_code`**: `preflight_not_green`; verbatim **`skip_reason`**), [`slice27_pytest_real_whisper_cpp_task91.log.txt`](slice27/slice27_pytest_real_whisper_cpp_task91.log.txt).

**Prior blocked posture (Task 134–136 — superseded by PASS above):** [`slice27_preflight_task134_live.json`](slice27/slice27_preflight_task134_live.json), **Task 137** table below (historical).

## Task 38 — operator completion record

| Field | Value |
| --- | --- |
| **As of** | **2026-04-24** — **Tasks 175–181** operator session on **`http://127.0.0.1:18293`** (dedicated port; `/health` proven **200** before pytest). |
| **Matrix `whisper_cpp` runtime transcript** | **PASS** — [`ENGINE_PARITY_MATRIX.md`](ENGINE_PARITY_MATRIX.md) row updated; overrides + `engine_truth` v1+v2 regenerated (**§8** this batch). |
| **Committed `slice27_transcribe_response.json`** | **Yes** — [`slice27/session_20260424_175_181_18293/slice27_transcribe_response.json`](slice27/session_20260424_175_181_18293/slice27_transcribe_response.json) (same session as **Task 176** preflight + **Task 177** pytest log). |

## Task 46 — operator / agent session record

| Field | Value |
| --- | --- |
| **As of** | **2026-04-24** — superseded by **Tasks 143–148** PASS session (see **Task 59** / **Task 148**). |
| **Outcome** | **PASS** — runtime transcript closure recorded in this doc + matrix + manifest. |
| **Next** | Maintain **§8** discipline for future engine rows; RHVoice freeze unchanged (**Task 148** policy). |

## Tasks 50–56 — link / guard session (Task 52 operator posture)

| Field | Value |
| --- | --- |
| **As of** | 2026-04-24 — **Tasks 50–56** (truth-doc link integrity, markdown link test, override reference test, `manifest_consistency_ok` map, STS governance Option A in matrix, RHVoice freeze audit). **No** new `real_whisper_cpp` run on agent host (GGUF / binding still not satisfied for non-skip transcript). |
| **Outcome** | **Historical snapshot (this batch only):** matrix **`whisper_cpp`** runtime transcript was **pending** on the agent host; no committed `slice27_transcribe_response.json` in that batch. **Superseded:** **Tasks 175–181** closed runtime transcript **PASS** — see **## Task 59** table + [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md). |
| **Next** | For **new** hosts only: operator with GGUF + green `checks.whisper_cpp` → [slice27/README.md](slice27/README.md) **§1–§8** (regression / re-audit — **Tasks 175–181** already closed primary seam). |

## Tasks 57–63 — governance batch (summary)

| Field | Value |
| --- | --- |
| **As of** | 2026-04-24 — **Tasks 57–63** (verify-bar date alignment + `test_truth_session_verify_date_alignment.py`; STT pack + pack contract; `gpt_sovits` → `engine_kind: vc` + narrow `vc` map; `ENGINE_PARITY_MATRIX.md` in markdown link contract; tortoise matrix link target; **Task 59** / **Task 60** posture tables immediately below; RHVoice freeze audit). |

## Task 59 — `real_whisper_cpp` runtime transcript (Tasks 57–63)

| Field | Value |
| --- | --- |
| **As of** | **2026-04-24** — **Tasks 175–181:** **175** listening backend + `/health` **200**; **176** green `checks.whisper_cpp.ok` + operator preflight JSON **18293**; **177** **`real_whisper_cpp` 1 passed, 0 skipped**; **178** §8 (this doc + matrix + overrides + `generate_engine_truth` + STATE + verify bar per policy); **179** skipped (PASS); **180–181** ledger sweep + RHVoice posture unchanged. **Tasks 142–148 (18085)** remain prior archaeology. |
| **Outcome** | **PASS** — session [`slice27_transcribe_response.json`](slice27/session_20260424_175_181_18293/slice27_transcribe_response.json); preflight [`slice27_preflight_task175_176_18293.json`](slice27/session_20260424_175_181_18293/slice27_preflight_task175_176_18293.json); pytest log [`slice27_pytest_real_whisper_cpp_task177.log.txt`](slice27/session_20260424_175_181_18293/slice27_pytest_real_whisper_cpp_task177.log.txt); manifest [`slice27_live_session_manifest.json`](slice27/session_20260424_175_181_18293/slice27_live_session_manifest.json). Default weights **`ggml-medium.en.bin`** + **`whisper-cli`** 1.8.4 (`tools/whispercpp/`). |
| **Next** | Re-run operator ladder only when changing weights / CLI / manifest contract; keep **session-scoped** artifact dirs for new ports. |

### Task 137 — Historical frozen blocker (Task 134–136 session — superseded by Task 148)

When documenting **pre-143** skips, cite **only** this block (no extra narrative):

| Field | Value |
| --- | --- |
| **Base URL** | `http://127.0.0.1:18082` (**Task 134** / **136** session; dedicated port — not default **:8000**) |
| **`blocked_reason_code`** | `preflight_not_green` |
| **`skip_reason` (verbatim)** | `Preflight checks.whisper_cpp.ok is not true — see ensure_whisper_cpp / GGUF / binding or CLI (Slice 22).` (from [`slice27_session_meta.json`](slice27/slice27_session_meta.json)) |
| **Artifacts** | [`slice27_preflight_task134_live.json`](slice27/slice27_preflight_task134_live.json), [`slice27_preflight_task134_session.txt`](slice27/slice27_preflight_task134_session.txt), [`slice27_preflight_capture.json`](slice27/slice27_preflight_capture.json) (pytest), [`slice27_session_meta.json`](slice27/slice27_session_meta.json), [`slice27_pytest_real_whisper_cpp_task136.log.txt`](slice27/slice27_pytest_real_whisper_cpp_task136.log.txt) |
| **GGUF / binding** | **`checks.whisper_cpp.message`:** `Whisper.cpp model missing at C:\ProgramData\VoiceStudio\models\whisper\whisper-medium.en.gguf. Place the GGUF or enable auto-download.` — Uvicorn log: **`whisper-cpp-python` not installed** (this host). |

### Task 148 — PASS session manifest (machine-readable)

| Field | Value |
| --- | --- |
| **Manifest** | [`slice27_live_session_manifest.json`](slice27/slice27_live_session_manifest.json) |
| **Outcome** | **`pass`** — **`recorded_utc`** per manifest; paths relative to repo root. |

## Task 60 — Post-PASS mechanical flip (**done** — Task 145)

| Field | Value |
| --- | --- |
| **Status** | **Completed (2026-04-24)** — [slice27/README.md](slice27/README.md) **§8** executed after **Task 144** non-skip **PASS**: `ENGINE_PARITY_MATRIX.md` **`whisper_cpp`** row, `engine_truth_overrides.json`, `python scripts/generate_engine_truth.py` + `--schema v2`, **STATE** verify bar + **LATEST PROOF INDEX**, anchored **`verify.ps1`** artifact (**Task 147**). Alias map: **## Task ID map (archived aliases)**. |

## Tasks 166–174 — Agent harness same-session attempt (**blocked** — `health_connect_failed`)

| Field | Value |
| --- | --- |
| **As of** | **2026-04-24** — one explicit base URL **`http://127.0.0.1:18081`** (not **:8000**); **`VOICESTUDIO_SLICE27_ARTIFACT_DIR`** pointed at repo `docs/reports/verification/slice27` for pytest artifact writes; blocker bundle moved under dedicated subfolder (no mixing with **Task 143** PASS files). |
| **Outcome** | **`pytest -m real_whisper_cpp`:** **1 skipped**, **0 passed** — **`blocked_reason_code`:** `health_connect_failed` (no listener on **18081** this session). **§8** batch **not** run (no non-skip transcript). |
| **Artifacts (same session)** | [`task166_174_20260424_blocker/slice27_session_meta.json`](slice27/task166_174_20260424_blocker/slice27_session_meta.json), [`task166_174_20260424_blocker/slice27_preflight_operator_task166_174.json`](slice27/task166_174_20260424_blocker/slice27_preflight_operator_task166_174.json), [`task166_174_20260424_blocker/slice27_pytest_real_whisper_cpp_task166_174.log.txt`](slice27/task166_174_20260424_blocker/slice27_pytest_real_whisper_cpp_task166_174.log.txt). |
| **Authority** | **Superseded for transcript closure** by **Tasks 175–181 PASS** on **18293** (see **Tasks 175–181** section below). This row remains **honest frozen** evidence for the **18081** no-listener attempt (not recycled **Task 143** JSON). |

## Tasks 175–181 — Operator discipline + live re-proof (**PASS**)

| Field | Value |
| --- | --- |
| **As of** | **2026-04-24** |
| **Base URL** | **`http://127.0.0.1:18293`** — dedicated Uvicorn; **`/health`** **200** before pytest (**Task 175**). |
| **Preflight** | **`checks.whisper_cpp.ok: true`** — operator capture [`slice27_preflight_task175_176_18293.json`](slice27/session_20260424_175_181_18293/slice27_preflight_task175_176_18293.json) (**Task 176**). |
| **Pytest** | **`pytest -m real_whisper_cpp`** — **1 passed, 0 skipped**; log [`slice27_pytest_real_whisper_cpp_task177.log.txt`](slice27/session_20260424_175_181_18293/slice27_pytest_real_whisper_cpp_task177.log.txt) (**Task 177**). |
| **Interpreter note** | Backend **Python 3.11** (Uvicorn); pytest **Python 3.9** (full `pytest.ini` plugins) — **same HTTP base URL** and same live process (**Task 175** discipline). |
| **§8** | **Task 178** — `ENGINE_PARITY_MATRIX.md`, `engine_truth_overrides.json`, `generate_engine_truth.py` v1+v2, **STATE** proof index + verify bar when anchored. |

## Open seams

- **Runtime + §8:** **Closed** for **`whisper_cpp`** transcript (**Tasks 175–178**; prior **143–148** archaeology retained); **Task ID map** retains **68/74/81/…** ↔ **69/75/82/…** aliases.
- **STT pack counts (Tasks 78–80):** Canonical only in [`generated/stt_hardening_regress_summary.json`](generated/stt_hardening_regress_summary.json) — no hand-typed **`passed_count`** in STATE/PROOF/matrix.
- **Governance ledger:** **STATE** **LATEST MILESTONE** / **LATEST PROOF INDEX** — **`test_state_ledger_contract.py`** — newest batches first (**123–132** before **114–122**, then **106–113**, **98–105**, …).
- **Verify bar (Tasks 96 / 104 / 112 / 121 / 131):** Bump **`latest_verify_artifact`** only with anchored **`verify.ps1`** + canonical truth batch — [`generated/README.md`](generated/README.md).
- **RHVoice (Tasks 70 / 77 / 83 / 97 / 105 / 113 / 122 / 132):** No **`engines/audio/rhvoice/`** churn without **`rhvoice-cli`** or **`executable_path`** proof; do not bundle with Slice 27 PRs.
- **Authority links (**Task 76**):** **`test_truth_doc_markdown_links.py`** — **STATE** + **CANONICAL_REGISTRY** allowlist.
- **ADR-056:** Transcribe path uses **`engine: whisper_cpp`** only (asserted in test).
