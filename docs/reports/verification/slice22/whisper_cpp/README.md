# Slice 22 — `whisper_cpp` readiness operator notes

## What this folder is

Bounded **readiness** artifacts for **`engine_id: whisper_cpp`** (whisper.cpp / GGUF + CLI or `whisper-cpp-python`). **Not** a runtime transcript closure (no `real_whisper_cpp` in Slice 22).

## Re-proof checklist

1. **Interpreter:** repo `.venv` (or the same Python you use for production API).
2. **Preflight:** `GET /api/health/preflight` → `checks.whisper_cpp.ok` must be **`true` or `false`**, never **`null`**.
3. **Outcome A (green):** GGUF on disk at resolved `model_path` **and** at least one execution surface:
   - `import whisper_cpp` succeeds, **or**
   - `tools/whispercpp/whisper.exe` (or `parameters.executable_path`) exists and responds to `-h` / `--help` (non-shell probe).
4. **Outcome B (frozen red):** capture `checks.whisper_cpp` JSON here + update [PROOF_SLICE22_WHISPER_CPP_READINESS.md](../../PROOF_SLICE22_WHISPER_CPP_READINESS.md) §session.
5. **Regression bar:** after edits, `python scripts/run_verification.py` + `.\scripts\verify.ps1 -Quick` GREEN; update [.cursor/STATE.md](../../../../.cursor/STATE.md) **Latest verify artifact**.

## Contract

[VOICESTUDIO_BOUNDED_SLICE22_WHISPER_CPP_READINESS_CONTRACT.md](../../../../design/VOICESTUDIO_BOUNDED_SLICE22_WHISPER_CPP_READINESS_CONTRACT.md)
