# Slice 8 — UI proof (operator session)

The automated agent cannot launch the WinUI shell for a real screenshot. Complete this checklist on a dev machine with the backend running (**no** `VOICESTUDIO_TEST_MODE=stub`) and Coqui XTTS available.

## Checklist

1. Start backend; confirm `GET http://127.0.0.1:8000/api/health` returns 200.
2. Launch **VoiceStudio.App**; open **Voice Synthesis** panel.
3. Engine: **xtts_v2**; select a local profile; paste: `VoiceStudio slice eight real synthesis.`
4. Run **Synthesize**; confirm success UI and optional playback.
5. Save **screenshot** as `ui_synthesis_success.png` in this folder.
6. Save **WAV** from `GET /api/audio/file/{audio_id}` as `slice8_output.wav` in this folder.

When done, update [../PROOF_SLICE8_REAL_SYNTHESIS_XTTS_20260414.md](../PROOF_SLICE8_REAL_SYNTHESIS_XTTS_20260414.md) §3 with the command output dates and confirm §2 item 5 is satisfied with real files.
