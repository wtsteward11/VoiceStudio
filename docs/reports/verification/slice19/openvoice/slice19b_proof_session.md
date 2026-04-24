# Slice 19B — OpenVoice live-proof closure attempt (session log)

**UTC date:** 2026-04-21  
**Git commit (repo):** `56e4c10757126616f2f25e0fae3fc82ef23956c3`  
**Canonical proof URL:** `http://127.0.0.1:8032`  
**Environment:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8032` (same for pytest + `dotnet test`)

## Task 1 — Backend discipline

| Item | Value |
| --- | --- |
| Stale listener **8031** | `LISTENING` PID **14544** — `GET /api/health` OK, but **`checks.openvoice`** returned legacy shape `ok: null` + `reason: no public ensure_*…` (**not** current repo `health.py`, which calls `ensure_openvoice()`). **Do not use** for Slice 19B proofs without restart on current code. |
| Fresh listener **8032** | `py -3.11 -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8032` from repo root with `PYTHONPATH=E:\VoiceStudio`. Matches **current** preflight wiring. |

## Task 2 — Preflight (same URL as proofs)

**Request:** `GET http://127.0.0.1:8032/api/health/preflight`  
**Result:** `checks.openvoice.ok` **false** (boolean — current code path).

**Verbatim `checks.openvoice` body (JSON):**

```json
{
  "ok": false,
  "downloaded": false,
  "message": "OpenVoice import failed in venv_advanced_tts (E:\\VoiceStudio\\runtime\\venvs\\torch26\\Scripts\\python.exe): Traceback (most recent call last):\n  File \"<string>\", line 1, in <module>\nModuleNotFoundError: No module named 'openvoice'. Install the OpenVoice stack into that venv (see MyShell-OpenVoice docs).",
  "status_code": 503,
  "python_exe": "E:\\VoiceStudio\\runtime\\venvs\\torch26\\Scripts\\python.exe"
}
```

**Primary seam (Branch B):** **`ModuleNotFoundError: No module named 'openvoice'`** in **`runtime/venvs/torch26`** — advanced TTS venv exists but OpenVoice package not installed; **no** synthesis run (preflight gate).

## Task 3 — Python `real_openvoice`

**Command:**

```text
set VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8032
python -m pytest tests/integration/test_synthesis_openvoice_real.py -m real_openvoice -v --tb=short
```

**Result:** **2 skipped** (fixture requires `checks.openvoice.ok == true`). Wall clock ~23s.

## Task 4 — C# OpenVoice `LiveBackend`

**Command:**

```text
set VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8032
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OpenVoice&TestCategory=LiveBackend" --no-build
```

**Result:** **3 skipped** (`AssertOpenVoicePreflightOkAsync` → Inconclusive when `ok` is not true).

## Task 5 — Branch

**Branch B** — Runtime parity **not** closed. Matrix **`openvoice`** remains **pending PASS**. No WAV artifacts (preflight red before synthesis).

## Task 6 — Regression bar

Run after doc edits: `dotnet build …`, `python scripts/run_verification.py`, `.\scripts\verify.ps1 -Quick`; record artifact path in [`.cursor/STATE.md`](../../../.cursor/STATE.md).
