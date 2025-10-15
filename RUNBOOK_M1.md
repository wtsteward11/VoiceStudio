# VoiceStudio – M1 Kickoff Runbook

Date: 2025-10-14 13:48:01

This pack contains:
- Agent prompts for M1 (A–E)
- Job JSONs (Convert, VAD, ASR, Align, TTS, VC)
- Helper scripts (smoke-m1.ps1, regen-proto.ps1 templates, align fallback, SNR report)
- A state snapshot to “save our spot”

## What M1 delivers
- **Agent A (App Shell/UX):** Dataset Studio waveform with segment overlays. Reads `manifest.json`, `asr.json`, `aligned.json`.
- **Agent B (Core):** FFmpeg runner with friendly error mapping + content-hash cache so repeated Convert/VAD/ASR/Align/TTS/VC jobs skip work.
- **Agent C (PluginHost):** Safe Mode (disable third-party plugins), stricter manifest validation, and clear load logs.
- **Agent D (Workers/ML):** Add SNR/energy stats per segment; expose `--snr` option and write `snr_db` into `manifest.json` entries.
- **Agent E (CI/Build):** CI builds Core & Client, regenerates proto stubs (stub step OK), spins worker venv, runs smoke-m1, keeps artifacts 7d.

## How to use with your local repo
1) Keep working directory: `C:\VoiceStudio` (you already have it).
2) Keep Core listening on `http://127.0.0.1:5071` (h2c) – confirmed during M0.
3) Place/keep FFmpeg on PATH and Piper under `C:\VoiceStudio\tools\piper` (done).
4) Workers venv: `C:\VoiceStudio\workers\python\vsdml\.venv` (done).

## Quick smoke for M1
- Make/confirm a test MP3: `C:\VoiceStudio\projects\demo\raw\input.mp3` (you can generate with Piper).
- Run `C:\VoiceStudio\build\smoke-m0.ps1` first (you have it).
- Then run `SCRIPTS\smoke-m1.ps1` from this pack and verify outputs list + SNR field in `manifest.json`.

## Golden rule
Only Agent B updates `src/IPC/vsd.proto`. Others just regenerate stubs.
