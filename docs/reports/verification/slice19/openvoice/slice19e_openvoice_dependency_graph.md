# Slice 19E — OpenVoice dependency graph and unblock attempt (authoritative)

**Date:** 2026-04-21  
**Interpreter:** `runtime/venvs/torch26/Scripts/python.exe` (Windows cp311 — same as `ensure_openvoice`).  
**Baseline:** [slice19d_openvoice_package_provenance.md](slice19d_openvoice_package_provenance.md), [ADR-053](../../../../../docs/architecture/decisions/ADR-053-openvoice-advanced-tts-packaging-surface.md).

## 1 — Package source (verbatim)

| Field | Value |
| --- | --- |
| **Pinned spec** | `myshell-openvoice @ git+https://github.com/myshell-ai/OpenVoice.git@74a1d147b17a8c3092dd5430504bd83ef6c7eb23` |
| **Repo declaration** | [requirements_engines.txt](../../../../../requirements_engines.txt) (OpenVoice block) |

## 2 — Upstream `install_requires` (commit `74a1d147…`, `setup.py`)

Non-optional list from upstream (MIT):

- `librosa==0.9.1`
- `faster-whisper==0.9.0`
- `pydub==0.25.1`
- `wavmark==0.0.3`
- `numpy==1.22.0`
- `eng_to_ipa==0.0.2`
- `inflect==7.0.0`
- `unidecode==1.3.7`
- `whisper-timestamped==1.14.2`
- `pypinyin==0.50.0`
- `cn2an==0.5.22`
- `jieba==0.42.1`
- `gradio==3.48.0`
- `langid==1.1.6`

Source: `https://raw.githubusercontent.com/myshell-ai/OpenVoice/74a1d147b17a8c3092dd5430504bd83ef6c7eb23/setup.py`

## 3 — Resolver graph edge (pip dry-run, `torch26`)

**Command:**

```powershell
Set-Location E:\VoiceStudio
& "runtime\venvs\torch26\Scripts\pip.exe" install --dry-run "myshell-openvoice @ git+https://github.com/myshell-ai/OpenVoice.git@74a1d147b17a8c3092dd5430504bd83ef6c7eb23"
```

**Observed resolution chain (abridged):**

1. `myshell-openvoice` (PEP 517 metadata from repo).
2. Direct deps collected: `librosa==0.9.1`, `faster-whisper==0.9.0`, `wavmark==0.0.3`, `numpy==1.22.0`, … `whisper-timestamped==1.14.2`, `gradio==3.48.0`, …
3. **`faster-whisper==0.9.0`** → **`Collecting av==10.*`** (metadata constraint from faster-whisper wheel).
4. **`av==10.0.0`** selected from **sdist** (`av-10.0.0.tar.gz`) — no cp311 win_amd64 wheel on PyPI for `av` 10.x (see Slice 19D `--only-binary` probe).
5. **Failure:** `Getting requirements to build wheel` for **`av`** — **Cython `CompileError`** in **`av\logging.pyx`** (exception / `noexcept` vs newer Cython in isolated build env; verbatim tail matches Slice 19C / 19D).

**Exit code:** `1` (dry-run still executes metadata / wheel prep far enough to hit `av` build).

## 4 — Install-time vs VoiceStudio runtime path

| Question | Answer |
| --- | --- |
| **Is `faster-whisper` / `av` mandatory for `pip install` the stock package?** | **Yes** — `install_requires` lists `faster-whisper==0.9.0`; resolver pulls **`av==10.*`**. |
| **Does `openvoice.api` import `faster_whisper`?** | **No** — [upstream `openvoice/api.py`](https://raw.githubusercontent.com/myshell-ai/OpenVoice/74a1d147b17a8c3092dd5430504bd83ef6c7eb23/openvoice/api.py) imports torch, librosa, soundfile, internal `openvoice.*` modules only (fetched 2026-04-21). |
| **Does VoiceStudio’s engine use `se_extractor`?** | **Yes** — [app/core/engines/openvoice_engine.py](../../../../../app/core/engines/openvoice_engine.py) imports `se_extractor` and calls `se_extractor.get_se` (e.g. lines ~439, ~713, ~810). |
| **Does upstream `se_extractor` import `faster_whisper`?** | **Yes, at module top level** — `from faster_whisper import WhisperModel` in [upstream `openvoice/se_extractor.py`](https://raw.githubusercontent.com/myshell-ai/OpenVoice/74a1d147b17a8c3092dd5430504bd83ef6c7eb23/openvoice/se_extractor.py) (line ~10). Importing `openvoice.se_extractor` loads **`faster_whisper`** (and thus the **`av`** stack) before any function runs. |

**One-sentence §19E ruling:** **`faster-whisper`/`av` is mandatory for stock `pip install`** and **mandatory for VoiceStudio’s supported synthesis path** because **`se_extractor` is imported at engine load** and pulls **`faster_whisper` at import time** — Branch B “drop whisper from install only” is **not** safe without a **fork** that refactors `se_extractor` (lazy imports or optional tone pipeline).

## 5 — Active strategy (Task 3)

| Branch | Result this slice |
| --- | --- |
| **A** — Stock source, reproducible `torch26` install | **Exhausted for this host:** dry-run and prior 19C real `pip install` both fail on **`av==10.*`** source build (no binary wheel for Windows cp311). No undocumented vendor wheel introduced. |
| **B** — Fork / patch metadata or `se_extractor` | **Out of scope for 19E execution** — requires repo URL change + ADR-044 + engineering review; recorded as next unblocker if product accepts fork maintenance. |
| **C** — Evidence ADR-053 `torch26`-only stock install is not viable on Windows cp311 | **Selected outcome for governance:** reproducible resolver + build failure + runtime import analysis → **[ADR-054](../../../../../docs/architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md)** (**Proposed**) for isolated OpenVoice venv (ADR-052 class). |

**Active strategy label:** **C (evidence-backed; ADR-054 Proposed)** — not matrix PASS; not silent ADR-053 revision.

## 6 — Task 4 import probe (production-equivalent)

```text
& "E:\VoiceStudio\runtime\venvs\torch26\Scripts\python.exe" -c "from openvoice.api import BaseSpeakerTTS, ToneColorConverter"
→ ModuleNotFoundError: No module named 'openvoice'
```

Package never installed — **blocked upstream** as above.

## 7 — Task 5 checkpoints

Resolved models root default: `E:\VoiceStudio\models` when `VOICESTUDIO_MODELS_PATH` unset — **`E:\VoiceStudio\models\openvoice`** **absent** on proof host (same as 19C/19D).

## 8 — Tasks 6–7 (gates)

**Task 6 (preflight):** Fresh automated capture on **`127.0.0.1:8034`** (`py -3.11 -m uvicorn`, `PYTHONPATH=E:\VoiceStudio`) did **not** return HTTP from this agent session (**connection actively refused** — listener never bound / process exited early). **Representative** `checks.openvoice` body for current code + **`torch26`** probe remains **verbatim** in [slice19b_preflight_openvoice.json](slice19b_preflight_openvoice.json) (`ok: false`, `ModuleNotFoundError: No module named 'openvoice'`). **2026-04-21** direct probe: `torch26\Scripts\python.exe -c "from openvoice.api import …"` → **`ModuleNotFoundError`** (package not installed).

**Task 7 (live):** **Not run** — hard gate: no green preflight / no import success.

## 9 — Regression (Task 9)

Executed at slice closure — see [.cursor/STATE.md](../../../../../.cursor/STATE.md) **Last Verified Commands** and **Latest verify artifact**.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-21 | Initial Slice 19E: full dry-run graph, `setup.py` list, pip-vs-runtime analysis, strategy **C** + ADR-054 Proposed. |
