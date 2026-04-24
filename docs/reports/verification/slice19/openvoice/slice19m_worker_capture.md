# Slice 19M — OpenVoice worker evidence (one capture)

**Date:** 2026-04-20  
**Host:** Windows, repo `E:\VoiceStudio`, `runtime\venvs\openvoice\Scripts\python.exe` (venv_openvoice).

## 1) Command (as `_invoke_worker` + manual equivalent)

`cwd` = repo root, `PYTHONPATH` = repo root, `PYTHONIOENCODING` = `utf-8` (set in subprocess env in code; for manual runs set in shell too).

```text
<runtime/venvs/openvoice/Scripts/python.exe> -m app.cli.openvoice_worker_synthesize <request.json>
```

`request.json` fields (same shape as `OpenVoiceSubprocessEngine._invoke_worker` payload): `text`, `speaker_wav` (abs path to `tests/fixtures/audio/openvoice_reference_speech.wav`), `language`, `output_path` (e.g. `%TEMP%\vs_slice19m_worker_out.wav`), `base_speaker_model`, `tone_color_converter_model`, `device` (`cpu`), `gpu` (`false`), `enable_style_control`, `enhance_quality`, `calculate_quality`, `speed`.

## 2) Exit code

- **Pre-fix (19L-era):** `1` — `BaseSpeakerTTS.tts() missing 2 required positional arguments: 'output_path' and 'speaker'`.  
- **Post–19M code fixes:** `0` when synthesis completes; direct engine probe: output WAV **79916** bytes for `OpenVoiceSubprocessEngine.synthesize` with the same reference.

## 3) stderr / stdout (notes)

- Upstream OpenVoice logs **trailing** at ~2000 chars in `OpenVoiceSubprocessEngine` when `returncode != 0` (not the full stream).  
- `Engine router not available` and optional-deps warnings (resemblyzer, pyloudnorm, etc.) are **noise** from other modules importing; not the root synthesis failure.  
- With `PYTHONIOENCODING=utf-8` + worker `sys.stdout`/`sys.stderr` reconfigure, myshell’s sentence-split `print` no longer trips **charmap** on Windows.

## 4) `output_path` after run

- **Failing 19L-class run:** file **missing** or size **&lt; 64** (engine returns `None`).  
- **Success after 19M fixes:** file **exists**; example size **79916** bytes (≥ 64).

## 5) Branch (19M)

| Result | Branch |
|--------|--------|
| No WAV or exit ≠ 0 | **A** — debug worker / TTS+SE+convert (see §19M proof) |
| WAV present and service still 500 | **B** — `SynthesisService` / file-ready / artifact path |

**This capture (post-fix):** direct subprocess **engine** path produced a valid WAV → prior HTTP **500** with *engine returned None* on **19L** was **Branch A** (no valid file from worker). **Conclusion for `return None` + file contract:** **A** — `None` from the subprocess engine with a file **&gt; 0** bytes is the intended file-driven success; `SynthesisService` proceeds when `_synth_output_file_ready(output_path)` is true (see `synthesis_service.py`).

**N/A:** If only Branch A with worker never writing a file, the `None`+file **A/B** contract does not apply until a **200**+file run exists.

## 6) Re-run (ladder)

**2026-04-22 (8055):** `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8055`, `VOICESTUDIO_MODELS_PATH` = repo `models`, Uvicorn from `.venv` — **`pytest -m real_openvoice` 2/2 PASS**; **`dotnet test` … `OpenVoice&TestCategory=LiveBackend` 3/3 PASS**. Use **speech** reference (not 440 Hz).
