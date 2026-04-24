# Slice 21 / 21A — Whisper live transcript (operator notes)

**Contract (Slice 21 harness):** [VOICESTUDIO_BOUNDED_SLICE21_WHISPER_LIVE_TRANSCRIPT_PROOF.md](../../../../design/VOICESTUDIO_BOUNDED_SLICE21_WHISPER_LIVE_TRANSCRIPT_PROOF.md)  
**21A closure brief (success/fail table):** [VOICESTUDIO_BOUNDED_SLICE21A_WHISPER_TRANSCRIPT_CLOSURE.md](../../../../design/VOICESTUDIO_BOUNDED_SLICE21A_WHISPER_TRANSCRIPT_CLOSURE.md)  
**PROOF (harness + §21A runtime):** [PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md](../../PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md)  
**Matrix:** [ENGINE_PARITY_MATRIX.md](../../ENGINE_PARITY_MATRIX.md) (STT `whisper` row)

## One base URL

Set **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** to your running backend (no trailing slash; same variable as TTS `real_xtts` / `real_openvoice` proofs). Use **one** port for the whole session (preflight, upload, transcribe). Example:

```bat
set VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8066
```

## Gate

```text
curl -s %VOICESTUDIO_REAL_XTTS_HTTP_BASE%/api/health/preflight
```

Require `checks.whisper.ok: true` before running pytest. If false, stop and capture `message` (valid **Outcome B** material) — do not claim a runtime PASS.

## Test

```text
python -m pytest tests/integration/test_transcribe_whisper_real.py -v -m real_whisper --tb=short
```

## Artifacts (Outcome A — 2026-04-22 session on file)

| File | Purpose |
| --- | --- |
| [slice21a_proof_session.md](slice21a_proof_session.md) | Port, command, result |
| [slice21a_preflight_whisper.json](slice21a_preflight_whisper.json) | `checks.whisper` evidence |
| [slice21a_pytest_session.txt](slice21a_pytest_session.txt) | Full pytest stdout |
| [slice21a_transcribe_response.json](slice21a_transcribe_response.json) | Upload + `POST /api/transcribe/` JSON (actual transcript, `engine`, `segments`) |

Update **PROOF** session table + **.cursor/STATE.md** (LATEST PROOF INDEX, Latest verify artifact) after re-proofing. Run `dotnet build`, `python scripts/run_verification.py`, and `.\scripts\verify.ps1 -Quick` before closing governance edits.

## Re-proof on a new host or after environment change

Do **not** add a new slice number unless scope changes. Re-run the same 21A lane:

1. Start **one** Uvicorn from the repo venv: `python -m uvicorn backend.api.main:app --host 127.0.0.1 --port <port>`.
2. Set **`VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:<port>`** (identical for all HTTP calls).
3. **`GET /api/health/preflight`** — confirm **`checks.whisper.ok: true`** (install **faster-whisper** in that interpreter if not).
4. Run **`pytest -m real_whisper`** as above; **1 passed, 0 skipped** = Outcome A; any honest failure = document **Frozen seam** in PROOF and keep matrix honest.
5. Save new **`slice21a_*.md|json|txt`** in this folder (or versioned names), point **PROOF** §21A session record at them, refresh **STATE** and **ENGINE_PARITY_MATRIX** changelog if the matrix line changes.
6. **C#** remains a separate bounded slice if needed — not part of 21A.
