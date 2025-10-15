VoiceStudio – M1 Add‑Ons (Audio Converter + VAD)

1) Install FFmpeg:
  powershell -ExecutionPolicy Bypass -File C:\VoiceStudio\build\install-ffmpeg.ps1

2) Convert audio to 48k mono WAV:
  powershell -ExecutionPolicy Bypass -File C:\VoiceStudio\src\Core\convert.ps1 "C:\path\in.mp3" "C:\VoiceStudio\projects\demo\processed\in.wav"

3) Prepare Python venv and VAD deps:
  cd C:\VoiceStudio\workers\python\vsdml
  powershell -ExecutionPolicy Bypass -File dev-venv.ps1
  pip install -r vad_requirements.txt

4) Run VAD:
  cd C:\VoiceStudio\workers\python\vsdml
  python vad.py --in "C:\VoiceStudio\projects\demo\processed\in.wav" --out "C:\VoiceStudio\projects\demo\dataset\segments" --aggr 2 --min 0.6
