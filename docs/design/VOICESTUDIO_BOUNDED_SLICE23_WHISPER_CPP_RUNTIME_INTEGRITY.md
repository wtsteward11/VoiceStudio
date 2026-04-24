# Bounded Slice 23 — `whisper_cpp` runtime surface integrity + contract alignment

**Status:** Accepted (implementation)  
**Purpose:** Align **`WhisperCPPEngine`** runtime behavior with [Slice 22](VOICESTUDIO_BOUNDED_SLICE22_WHISPER_CPP_READINESS_CONTRACT.md) governance and [no-fallbacks.mdc](../../.cursor/rules/core/no-fallbacks.mdc): **no cross-engine substitution** into **`whisper`** (faster-whisper). Readiness truth remains **`ensure_whisper_cpp`** + boolean **`checks.whisper_cpp`**; this slice fixes **engine-layer purity only**.

## Engine id

**Authoritative id:** `whisper_cpp` (STT; whisper.cpp only).

## Hard prohibition

**`WhisperCPPEngine` must never** import, construct, or call **`WhisperEngine`** (or any faster-whisper path). Failure of whisper.cpp (binding, CLI, model, subprocess, parse) **stays** a **`whisper_cpp`** failure.

## Failure model (attribution)

Log and surface failures with **`engine_id=whisper_cpp`** (and stable reason tokens where useful):

| Category | Meaning |
| --- | --- |
| `missing_model` | GGUF / model path not on disk |
| `no_binding` | Python `whisper_cpp` not available and no usable `_ctx` |
| `binary_unavailable` | CLI not found or `--help` probe failed |
| `binary_failed` | subprocess non-zero or unusable output |
| `parse_failed` | JSON/text parse did not yield a transcript |

**Return contract:** `_perform_transcription` returns **`None`** on failure (no fake dict with empty `text`). Callers such as [`backend/services/transcription_service.py`](../../backend/services/transcription_service.py) map **`None`** to **HTTP 500** with engine attribution.

## In scope / out of scope

| In scope (Slice 23) | Out of scope |
| --- | --- |
| Remove faster-whisper fallback from [`app/core/engines/whisper_cpp_engine.py`](../../app/core/engines/whisper_cpp_engine.py) | `real_whisper_cpp` / live transcript proof (future slice when readiness is green) |
| Fail-closed transcription; explicit logs | **`vosk`** (STT) / **`parakeet`** (TTS) preflight slices **26** / **28** — not Slice 23 engine code |
| [`engines/audio/whisper_cpp/engine.manifest.json`](../../engines/audio/whisper_cpp/engine.manifest.json) metadata honesty | Registry-driven preflight refactor (`model_preflight` / `health.py`) |
| Unit tests: **`WhisperEngine` never invoked** on `whisper_cpp` paths | Machine-generated `engine_truth` JSON |
| Governance: PROOF, matrix, STATE, registry | **Router default STT fallback chain** — see §Known seam |

## Relation to Slice 22

- **Slice 22** closed **readiness** (preflight + probe + single `ensure_whisper_cpp`).
- **Slice 23** closes **runtime integrity** so operators are not misled by silent substitution after selecting **`whisper_cpp`**.

## Known seam (Slice 23 scope; **remediated in Slice 24**)

Historically: [`app/core/engines/router.py`](../../app/core/engines/router.py) walked a multi-engine STT chain. **Slice 24** (`VOICESTUDIO_BOUNDED_SLICE24_STT_ROUTER_FAIL_CLOSED.md`, **ADR-056**) enforces **single default STT**, **no load-based STT substitution**, **`faster_whisper` → `whisper` id alias** at `get_engine`, and **`explicit_engine_id`** on **`select_engine_with_fallback`**. See **Slice 24** proof for verification commands.

## Related artifacts

- Proof: [PROOF_SLICE23_WHISPER_CPP_RUNTIME_INTEGRITY.md](../reports/verification/PROOF_SLICE23_WHISPER_CPP_RUNTIME_INTEGRITY.md) (post-verify)  
- Matrix: [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md)  
- Readiness contract: [VOICESTUDIO_BOUNDED_SLICE22_WHISPER_CPP_READINESS_CONTRACT.md](VOICESTUDIO_BOUNDED_SLICE22_WHISPER_CPP_READINESS_CONTRACT.md)  

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-23 | **Closure:** [PROOF_SLICE23_WHISPER_CPP_RUNTIME_INTEGRITY.md](../reports/verification/PROOF_SLICE23_WHISPER_CPP_RUNTIME_INTEGRITY.md); `WhisperCPPEngine` fallback removed; manifest tier; matrix + **STATE**; verify [`20260423_015151`](../../artifacts/verify/20260423_015151/verification_report.md). |
| 2026-04-23 | Initial: prohibition, failure model, known seam, scope vs Slice 22. |
