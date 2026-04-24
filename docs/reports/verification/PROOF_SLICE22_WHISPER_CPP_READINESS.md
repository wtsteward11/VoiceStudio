# PROOF — Bounded Slice 22 (`whisper_cpp` readiness)

**Status:** Closed (readiness wiring + governance)  
**Date:** 2026-04-23  
**Scope:** Boolean **`checks.whisper_cpp`** on **`GET /api/health/preflight`**, **`ensure_whisper_cpp`** single source of truth in **`backend/services/model_preflight.py`**, **`engine_readiness_probe`** parity, **no** `real_whisper_cpp` / C# transcribe runtime in this slice.

## §22 — Readiness truth

| Item | Result |
| --- | --- |
| **`checks.whisper_cpp.ok` never `null`** | **PASS** — `health.py` wires explicit `ensure_whisper_cpp(auto_download=False)`; `_NO_PUBLIC_PREFLIGHT` no longer lists `whisper_cpp`. |
| **Single `ensure_whisper_cpp`** | **PASS** — canonical implementation in **`backend/services/model_preflight.py`**; **`backend/ml/models/model_preflight.py`** delegates to services; **`transcription_service`** imports from services. |
| **Green definition** | GGUF exists **and** (Python **`whisper_cpp`** import **or** whisper.cpp CLI probe). Health preflight uses **`auto_download=False`**. |
| **Probe parity** | **PASS** — `scripts/engine_readiness_probe.py` calls same **`ensure_whisper_cpp(auto_download=False)`**. |

## §22A — Operator session (preflight JSON)

**Method:** FastAPI **`TestClient`** against **`backend.api.main:app`** (same import graph as Uvicorn; avoids port collision).  
**Artifacts:** [`slice22/whisper_cpp/slice22_preflight_whisper_cpp.json`](slice22/whisper_cpp/slice22_preflight_whisper_cpp.json); session notes [`slice22/whisper_cpp/slice22_proof_session.md`](slice22/whisper_cpp/slice22_proof_session.md)

**Outcome B (this proof host):** `checks.whisper_cpp.ok: false` with **`status_code` 424** — resolved GGUF path under `get_models_path()` **missing** on disk. This is **honest readiness red**, not `ok: null`.

**Outcome A** is satisfied when operators place the GGUF (or run transcribe with `auto_download=True` out of band) **and** provide at least one execution surface per contract.

## Matrix

[`ENGINE_PARITY_MATRIX.md`](ENGINE_PARITY_MATRIX.md) — STT **`whisper_cpp`** row updated: boolean preflight + proof link; **runtime transcript PASS** remains **future** bounded work (distinct from **`whisper`** faster-whisper **21A**).

## Regression bar (closure session)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** (recorded post-change in **STATE**).
- `python scripts/run_verification.py` — **PASS**
- `.\scripts\verify.ps1 -Quick` — **VERIFICATION PASSED** — [`artifacts/verify/20260422_232054/verification_report.md`](../../../artifacts/verify/20260422_232054/verification_report.md) (mirrored in **STATE** **Latest verify artifact**)

## Related

- Design: [VOICESTUDIO_BOUNDED_SLICE22_WHISPER_CPP_READINESS_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE22_WHISPER_CPP_READINESS_CONTRACT.md)
- **Slice 20 / 21 / 21A** (`whisper` faster-whisper): unchanged; **no** automatic fallback to **`whisper`** on **`whisper_cpp`** failure.
