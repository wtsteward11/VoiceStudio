param([string]$Text = "Hello from VoiceStudio TTS",
      [string]$Out = "C:\VoiceStudio\projects\demo\tts\hello.wav")
$ErrorActionPreference = "Stop"
cd C:\VoiceStudio\workers\python\vsdml
. .\.venv\Scripts\Activate.ps1
pip install -r requirements_m3.txt
python tts_piper.py --text "$Text" --voice "C:\VoiceStudio\tools\piper\voices\en_US-amy-low.onnx" --out "$Out"
Write-Host "TTS done -> $Out"
