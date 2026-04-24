# Bounded Slice 22 — whisper.cpp readiness (preflight truth)

**Status:** Accepted (implementation)  
**Purpose:** Single source of truth for **`engine_id: whisper_cpp`**: **`ensure_whisper_cpp`** in `backend/services/model_preflight.py` and boolean **`checks.whisper_cpp`** on **`GET /api/health/preflight`**. This is **not** the **`whisper`** (faster-whisper) stack — see [VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md](VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md).

## Engine id

**Authoritative id:** `whisper_cpp` (STT; whisper.cpp / GGUF + binary or `whisper-cpp-python`).

## What Slice 22 does / does not do

| In scope (Slice 22) | Out of scope |
| --- | --- |
| Boolean **`checks.whisper_cpp`** (never **`ok: null`**) on preflight | Runtime STT “transcript PASS” / `pytest` live harness (future bounded slice, e.g. after green readiness) |
| **`ensure_whisper_cpp`** + GGUF path + one execution surface (import **or** binary) | `vosk`, `parakeet`, RHVoice churn |
| Governance: PROOF, matrix row, **STATE** | C# `TranscribeAudioAsync` LiveBackend |
| | Automatic fallback to **`whisper`** on failure (see `no-fallbacks.mdc`) |

## Runtime surface (truth)

| Piece | Source |
| --- | --- |
| **GGUF model** | Resolved from engine config `parameters.model_path` or default `{models_root}/whisper/whisper-medium.en.gguf` per `get_models_path()`. |
| **Execution (one of)** | (1) Python **`whisper_cpp`** (`whisper-cpp-python`) importable in the **API** interpreter, **or** (2) **`whisper.cpp` CLI** at resolved **`executable_path`** (manifest default `tools/whispercpp/whisper.exe` relative to repo root). |
| **Manifest** | [engines/audio/whisper_cpp/engine.manifest.json](../../engines/audio/whisper_cpp/engine.manifest.json) |

## Readiness — what `checks.whisper_cpp.ok == true` means

1. **GGUF** file exists at the resolved **`model_path`** (preflight uses **`auto_download=False`** on health — no silent download on every preflight; operators enable download at transcribe or run download out of band).  
2. **At least one** execution surface: **`import whisper_cpp`** succeeds, **or** the resolved **executable** exists on disk and responds to a non-shell subprocess probe (`--help` / `-h` as supported by the binary).

**`ok: false`** includes an explicit **`message`** (missing model, no binary/bindings, probe failure).

## venv

Manifest: **`venv_stt`**. Readiness uses the **same Python** as the FastAPI process, matching Slice 20’s pattern for STT venv.

## Related artifacts

- Proof: [PROOF_SLICE22_WHISPER_CPP_READINESS.md](../reports/verification/PROOF_SLICE22_WHISPER_CPP_READINESS.md)  
- Matrix: [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) (STT `whisper_cpp` row)  
- Code: `backend/api/routes/health.py` (`checks["whisper_cpp"]`), `backend/services/model_preflight.py` (`ensure_whisper_cpp`), `scripts/engine_readiness_probe.py`  

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-23 | **Closure:** [PROOF_SLICE22_WHISPER_CPP_READINESS.md](../reports/verification/PROOF_SLICE22_WHISPER_CPP_READINESS.md); [slice22/whisper_cpp/](../reports/verification/slice22/whisper_cpp/) session JSON + README; [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) STT row; `health.py` + `ensure_whisper_cpp` hardening + probe; services-only `ensure_whisper_cpp` with ML delegate. |
| 2026-04-23 | Initial: boolean preflight, distinct from `whisper`; execution surface table; Slice 22 readiness only. |
