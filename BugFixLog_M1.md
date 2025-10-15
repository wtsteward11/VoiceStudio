# VoiceStudio – Bug & Fix Log (through M1 kickoff)

- Core bound to HTTP/2 h2c on :5071; Client uses Http2UnencryptedSupport.
- gRPC proto: fields PascalCase in C#, adjusted service impl accordingly.
- Worker TTS uses Piper with voice: tools\piper\voices\en_US-amy-low.onnx (+ .json).
- FFmpeg convert script path + args verified (48k mono s16).
- faster-whisper on CPU set to compute_type=int8.
- whisperx align fixed: use load_align_model(language_code="en", device="cpu", compute_type="int8"); removed old 'language' kwarg.
- Rehydrate script: PowerShell-safe (no heredoc), fixed Test-Path condition, fixed python version probe.
- Port-in-use lock resolved with taskkill + clean build.
- Smoke pipeline produces: processed\input.wav, dataset\segments\manifest.json, asr.json, aligned.json, tts\hello.wav, vc\shifted.wav.

Current status: Core↔Client Health/Run OK; all six job shapes defined; Piper installed; voices present; worker venv OK.
Next: M1 — Job queue + DB (Core), daemonized worker, basic UI stubs, CI wiring.
