# Slice 19C — OpenVoice `torch26` provisioning session (Branch B)

**UTC date:** 2026-04-21  
**Repo:** VoiceStudio (agent session)  
**Goal:** Install `myshell-openvoice` into `runtime/venvs/torch26` per [requirements_engines.txt](../../../../requirements_engines.txt) pin; then checkpoints + preflight + live proofs.

## Authoritative surface (frozen)

- **`venv_advanced_tts`** → **`E:\VoiceStudio\runtime\venvs\torch26\Scripts\python.exe`**
- **Not** backend `.venv` for OpenVoice preflight (`ensure_openvoice`).

## Command attempted

```powershell
Set-Location E:\VoiceStudio
& "runtime\venvs\torch26\Scripts\pip.exe" install "myshell-openvoice @ git+https://github.com/myshell-ai/OpenVoice.git@74a1d147b17a8c3092dd5430504bd83ef6c7eb23"
```

**Exit code:** `1` (failure).

## Primary seam (verbatim failure class)

Resolver pulled **`faster-whisper==0.9.0`** → **`av==10.*`** (source build). **Wheel build for `av` failed** with **Cython `CompileError`** in `av\logging.pyx` (exception / `noexcept` signature mismatch under the build env’s Cython vs PyAV 10). Pip ended with:

`error: subprocess-exited-with-error` — **Getting requirements to build wheel** for **`av`** did not run successfully.

**Interpretation:** OpenVoice’s pinned dependency chain is **not installable** into this **`torch26`** interpreter/toolchain without dependency surgery (e.g. newer `faster-whisper`/`av` pins, prebuilt wheels, or a dedicated OpenVoice venv policy). **Do not** claim matrix PASS or green preflight until install succeeds.

## Import probe (after failed install)

```text
ModuleNotFoundError: No module named 'openvoice'
```

(Unchanged from Slice 19B until package installs.)

## Checkpoint layout (same session)

`VOICESTUDIO_MODELS_PATH` defaulted to **`E:\VoiceStudio\models`** for this check:

| Path | Exists |
| --- | --- |
| `E:\VoiceStudio\models\openvoice` | **False** |
| `...\openvoice\base_speakers` | **False** |
| `...\openvoice\converter` | **False** |

Even if pip had succeeded, **`ensure_openvoice`** would still fail checkpoint validation until operator-supplied trees exist per [VOICESTUDIO_BOUNDED_SLICE19_OPENVOICE_SUPPORT_CONTRACT.md](../../../../design/VOICESTUDIO_BOUNDED_SLICE19_OPENVOICE_SUPPORT_CONTRACT.md).

## Live proofs

**Not run** — provisioning Branch B; preflight would remain red. No `pytest -m real_openvoice` / no C# OpenVoice `LiveBackend` this session.

## Next honest actions

1. Resolve **`av` / `faster-whisper` / OpenVoice** install strategy for **`torch26`** (reproducible pins, wheels, or ADR-scoped venv split) — **before** matrix or runtime PASS claims.
2. Populate **`openvoice/base_speakers`** and **`openvoice/converter`** under the real models root per `ensure_openvoice` rules.
3. Fresh Uvicorn + **`checks.openvoice.ok: true`** → Python → C# on one `VOICESTUDIO_REAL_XTTS_HTTP_BASE`.
