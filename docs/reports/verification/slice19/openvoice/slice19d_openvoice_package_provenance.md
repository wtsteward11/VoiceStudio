# Slice 19D — OpenVoice package provenance (authoritative)

**UTC date:** 2026-04-21  
**Purpose:** Freeze exact install path, interpreter, and failure chain for **Slice 19C / 19D** — no abstract “OpenVoice failed.”

## Package specification (repo contract)

| Field | Value |
| --- | --- |
| **Pinned spec** | `myshell-openvoice @ git+https://github.com/myshell-ai/OpenVoice.git@74a1d147b17a8c3092dd5430504bd83ef6c7eb23` |
| **Declared in** | [`requirements_engines.txt`](../../../../../requirements_engines.txt) (OpenVoice comment block) |
| **Engine manifest** | [`engines/audio/openvoice/engine.manifest.json`](../../../../../engines/audio/openvoice/engine.manifest.json) — `venv_family`: **`venv_advanced_tts`** |

## Interpreter (preflight authority)

| Field | Value |
| --- | --- |
| **Family** | `VenvFamily.ADVANCED_TTS` → provision dir **`torch26`** |
| **Windows executable** | `E:\VoiceStudio\runtime\venvs\torch26\Scripts\python.exe` |
| **Python version (observed)** | **3.11.9** |
| **Same as** | `ensure_openvoice` subprocess import probe in [`backend/services/model_preflight.py`](../../../../../backend/services/model_preflight.py) via `_require_venv_advanced_tts_python_exe(consumer="OpenVoice")` |

**Not authoritative for OpenVoice preflight:** backend repo `.venv` (FastAPI worker).

## Command attempted (Slice 19C — verbatim class)

```powershell
Set-Location E:\VoiceStudio
& "runtime\venvs\torch26\Scripts\pip.exe" install "myshell-openvoice @ git+https://github.com/myshell-ai/OpenVoice.git@74a1d147b17a8c3092dd5430504bd83ef6c7eb23"
```

**Exit code:** `1`.

## Upstream `install_requires` (commit `74a1d147…`)

Cloned **myshell-ai/OpenVoice** at **`74a1d147b17a8c3092dd5430504bd83ef6c7eb23`**; **`setup.py`** (setuptools) includes **non-optional** dependencies, including:

- `faster-whisper==0.9.0`
- `librosa==0.9.1`
- `numpy==1.22.0`
- … (full list in upstream `setup.py` in that commit)

There is **no** extras-based opt-out for `faster-whisper` in that `setup.py` — it is **`install_requires`**, not optional extras.

## Transitive failure (observed)

1. Resolver collects **`faster-whisper==0.9.0`**.
2. That pulls **`av==10.*`** (source / sdist path on this platform).
3. **Wheel build for `av`** fails: **Cython `CompileError`** in **`av\logging.pyx`** (exception / `noexcept` mismatch with build Cython vs PyAV 10).

Pip ends with: **`error: subprocess-exited-with-error`** — **Getting requirements to build wheel** for **`av`** did not run successfully.

## Platform / wheel fact (Slice 19D verification)

On **Windows cp311**, **`pip install av==10.0.0 --only-binary=:all:`** reports **no matching distribution** — available **`av`** wheels start at **12.x** for this ABI.

**Dry-run:** `pip install "faster-whisper>=1.0" --dry-run` resolves to **`faster-whisper-1.2.1`** + **`av-17.0.1`** **cp311 win_amd64 wheel** — i.e. a **newer** faster-whisper stack **does** get binary `av`, but **conflicts** with upstream OpenVoice’s **pinned** `faster-whisper==0.9.0`.

## Conclusion (for §19D / ADR-053)

- **VoiceStudio engine adapter** ([`app/core/engines/openvoice_engine.py`](../../../../../app/core/engines/openvoice_engine.py)) imports **`openvoice`**, **`se_extractor`**, **`BaseSpeakerTTS`**, **`ToneColorConverter`** — **no** direct `faster_whisper` / `av` imports.
- **Stock MyShell-OpenVoice package** nonetheless **mandates** **`faster-whisper==0.9.0`** → **`av==10.*`**, which is **not installable** on **Windows Python 3.11** with current PyPI wheels (no `av` 10 binary; source build fails as observed).

Hence: this is **not** “optional dependency over-pull”; it is **upstream install_requires vs platform wheel reality**.

## Checkpoint state (same host)

Under default models root **`E:\VoiceStudio\models`**: **`openvoice/`**, **`base_speakers`**, **`converter`** — **absent** (Slice 19C session). Even with a successful pip install, **`ensure_openvoice`** would still require these trees per contract.

## Slice 19D plan tasks 4–7 (closure record)

Per bounded Slice **19D** plan: these steps are recorded **honestly** — **no** fake green.

| Plan task | Status |
| --- | --- |
| **Task 4 — Install + import probe** | **Blocked** — same **`pip`** / **`av`** failure class as Slice 19C; **`from openvoice.api import BaseSpeakerTTS, ToneColorConverter`** not proven green in **`torch26`**. |
| **Task 5 — Checkpoints** | **Not satisfied on host** — **`models/openvoice/base_speakers`** and **`.../converter`** still absent under operator models root (no weights committed in repo). |
| **Task 6 — Fresh Uvicorn + preflight** | **Not executed** — plan hard gate: do **not** re-run until Task 4 green; expected outcome would match §19B (`checks.openvoice.ok: false`) until install + checkpoints. |
| **Task 7 — Live proof** | **Not executed** — gated on Task 6. |

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-21 | Initial Slice 19D provenance freeze + upstream `setup.py` confirmation + `av` wheel probe. |
| 2026-04-20 | Plan tasks 4–7 closure table (blocked / not run); ADR-053 + proof §19D cross-links. |
