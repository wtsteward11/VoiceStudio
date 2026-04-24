# Slice 19I — OpenVoice Strategy B (session log)

**Status:** Closed — **Outcome B** for **§19A live ladder** (2026-04-22) — **Strategy B** install + import + **`checks.openvoice.ok`** green; **`real_openvoice` 2/2** + C# **3/3** **red** (synthesis **500**).  
**Brief:** [VOICESTUDIO_BOUNDED_SLICE19I_OPENVOICE_STRATEGY_B_RUNTIME.md](../../../../design/VOICESTUDIO_BOUNDED_SLICE19I_OPENVOICE_STRATEGY_B_RUNTIME.md)  
**ADR:** [ADR-055](../../../../architecture/decisions/ADR-055-myshell-openvoice-vendored-patches.md)

## 1. Provisioning

- **Command:** `python scripts/engines/create_engine_venv.py --family openvoice --force`
- **Result:** **exit 0** after fixing **`pip install -e`** to pass **`-e`** and **path** as separate arguments; **`numpy>=1.24.0,<2.0`** to match vendored **`setup.py`**.

## 2. Import probes (`runtime\venvs\openvoice\Scripts\python.exe`)

- `from openvoice.api import BaseSpeakerTTS, ToneColorConverter` — **ok**
- `PYTHONPATH=e:\VoiceStudio` — `from app.core.engines.openvoice_engine import OpenVoiceEngine` — **ok** (warnings from optional audio deps only)
- `from openvoice import se_extractor` — **ok** (no top-level **`faster_whisper`**)

## 3. Checkpoints / preflight

- **`VOICESTUDIO_MODELS_PATH`:** `e:\VoiceStudio\models` — **placeholder** `config.json` + empty `checkpoint.pth` under `openvoice/base_speakers/EN` and `openvoice/converter` (satisfies file-layout probe only).
- **Backend:** `http://127.0.0.1:8040` (Uvicorn from **`.venv`**, `PYTHONPATH` = repo, same models path).
- **Preflight artifact:** [`slice19i_preflight_openvoice.json`](slice19i_preflight_openvoice.json) — **`checks.openvoice.ok: true`**.

## 4. Live proofs

- **`pytest -m real_openvoice`:** **2 failed** — `POST /api/voice/synthesize` **500** — `Synthesis failed - engine returned None...`
- **`dotnet test`:** filter `TestCategory=LiveBackend&FullyQualifiedName~OpenVoice` — **3 failed** (same message via `VoiceSynthesisService`).

## 5. Outcome (live ladder)

- **B** — Primary seam for **matrix**: synthesis **500** with **non-valid** checkpoint bytes; `SynthesisService` path `result is None` + no **`_synth_output_file_ready`** → **500** (see `synthesis_service.py` ~605–636). **Conclusion B** for file-contract task: not the **file-ready + `None`** success path.
- **Matrix `openvoice`:** **pending** (per plan: no **PASS** without **2/2** + **3/3** green).

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-22 | Placeholder created for Strategy B / ADR-055. |
| 2026-04-22 | **Closed** — evidence above; **Strategy A** superseded for this host by **vendor + ADR-055**. |
