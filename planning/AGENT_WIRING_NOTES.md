# Agent Wiring (M2 preview)

Agent B (Core orchestrator):
- Accepts an incoming Job (as above).
- Validates paths (InPath exists when required).
- Spawns Worker command:
  C:\VoiceStudio\workers\python\vsdml\.venv\Scripts\python.exe <script.py> <flags>
- Captures stdout/stderr; maps exit code → E_*; returns RunResponse.

Agent D (Worker):
- Can expose a local gRPC Run(Job) OR a simple process dispatcher now.
- Maps Job.Type → one of:
    vad.py
    asr_transcribe.py
    align_whisperx.py   (fixed language_code + CPU)
    tts_piper.py        (stdin → piper.exe)
    vc_pitch.py         (placeholder)
- Emits the minimal contract JSON on stdout and exits 0/1.

Queue (later):
- Table: Jobs(Id, Type, PayloadJson, Status, CreatedAt, StartedAt, EndedAt, OutputJson, ErrorCode, ErrorMsg)
- B pulls from queue; D processes; B updates status.

Keep current M1 behavior:
- Client → Core.Run → spawn script → respond.
- Wire the queue in M2; no breaking changes to job shapes.
