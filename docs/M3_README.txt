VoiceStudio – M3 Add‑Ons (TTS + Voice Conversion stub)

Quick steps:
1) Install Piper (free offline TTS) — one time
   powershell -ExecutionPolicy Bypass -File C:\VoiceStudio\build\install-piper.ps1

2) TTS from text
   cd C:\VoiceStudio\workers\python\vsdml
   . .\.venv\Scripts\Activate.ps1
   pip install -r requirements_m3.txt
   python tts_piper.py --text "Hello from VoiceStudio" --voice "C:\VoiceStudio\tools\piper\voices\en_US-amy-low.onnx" --out "C:\VoiceStudio\projects\demo\tts\hello.wav"

3) (Optional) Voice conversion stub (pitch shift placeholder)
   python vc_pitch.py --in "C:\VoiceStudio\projects\demo\processed\input.wav" --out "C:\VoiceStudio\projects\demo\vc\shifted.wav" --semitones 3
