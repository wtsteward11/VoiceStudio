$ErrorActionPreference="Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

# Ensure venv
if (!(Test-Path ".venv")) { python -m venv .venv }
$py = Join-Path $root ".venv\Scripts\python.exe"
& $py -m pip install -U pip wheel

# CPU torch to avoid CUDA issues
& $py -m pip install --index-url https://download.pytorch.org/whl/cpu torch torchaudio --upgrade
# WhisperX + deps
& $py -m pip install whisperx transformers accelerate librosa sentencepiece soundfile rich

# Tiny test WAV
$newWav = Join-Path $root "data\input\sample.wav"
New-Item -ItemType Directory -Force -Path (Split-Path $newWav) | Out-Null
$code = @"
import math, wave, struct, os
p=r'''$newWav'''
os.makedirs(os.path.dirname(p), exist_ok=True)
sr=16000; dur=2.0; f=440.0; n=int(sr*dur)
with wave.open(p,'w') as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
    for i in range(n):
        s=int(32767*0.2*math.sin(2*math.pi*f*i/sr))
        w.writeframes(struct.pack('<h', s))
print('wrote', p)
"@
& $py -c $code

Write-Host "✅ Align deps installed and sample.wav created." -ForegroundColor Green
pwsh -NoProfile -File ".\tools\run_app.ps1" -App align_demo
