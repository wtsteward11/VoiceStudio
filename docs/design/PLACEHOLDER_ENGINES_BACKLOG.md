# Placeholder Engines Implementation Backlog

Per **Items 21-40 Content Creator Wedge** (Item 36): all placeholder engines are to be implemented and integrated professionally—not disabled. This document tracks the 18 placeholder engines and the implementation steps.

## Audio (13) — Content-creator wedge priority

| Engine ID       | Manifest Path | Status   | Notes |
|-----------------|---------------|----------|-------|
| fish_speech     | engines/audio/fish_speech | placeholder | Zero-shot cloning; Fish Audio API/sdk |
| higgs_audio     | engines/audio/higgs_audio | placeholder | Higgs Audio TTS |
| lyrebird        | engines/audio/lyrebird | placeholder | Descript/Lyrebird (cloud/local) |
| mars5           | engines/audio/mars5 | placeholder | MARS5 TTS |
| openvoice_v2    | engines/audio/openvoice_v2 | placeholder | OpenVoice v2 |
| parler_tts      | engines/audio/parler_tts | placeholder | Parler TTS |
| voice_ai        | engines/audio/voice_ai | placeholder | Voice.ai |
| voxcpm          | engines/audio/voxcpm | placeholder | VoxCPM |
| coqui_tts       | engines/audio/coqui_tts | placeholder | Coqui TTS (legacy) |
| styletts2       | engines/audio/styletts2 | placeholder | StyleTTS 2 |

(Plus any other audio engines with `implementation_status: "placeholder"` in their manifest.)

## Image (4)

| Engine ID        | Manifest Path | Status   |
|------------------|---------------|----------|
| fastsd_cpu       | engines/image/fastsd_cpu | placeholder |
| openjourney      | engines/image/openjourney | placeholder |
| realistic_vision | engines/image/realistic_vision | placeholder |
| sd_cpu           | engines/image/sd_cpu | placeholder |

## Video (1)

| Engine ID     | Manifest Path | Status   |
|---------------|---------------|----------|
| video_creator | engines/video/video_creator | placeholder |

## Implementation steps (per engine)

1. **Install the library** — Add dependency to engine manifest and document in SETUP_GUIDE if needed.
2. **Implement the adapter** — In `app/core/engines/<name>_engine.py`, implement against the real library API following the `BaseEngine` / `EngineProtocol` contract.
3. **Update manifest** — Set `implementation_status: "full"` (or `"basic"` if partial).
4. **Add integration test** — At least one test in `tests/` that runs the engine (or is skipped when dependencies are missing).

## Priority order

1. Audio engines that support the content-creator wedge (TTS, cloning, enhancement).
2. Remaining audio placeholders.
3. Image and video placeholders.

## Reference

- Wedge definition: `docs/design/WEDGE.md`
- Engine manifest schema: `shared/schemas/engine_manifest_v3.schema.json`
- Base protocol: `app/core/engines/base.py` / `app/core/engines/protocols.py`
