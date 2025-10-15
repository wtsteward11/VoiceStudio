# Smoke M1: assumes Core is running on 127.0.0.1:5071 and Client exists.
param(
  [string]$JobsDir = "C:\VoiceStudio\planning\jobs"
)

Write-Host "== M1 smoke: Convert -> VAD -> ASR -> Align -> TTS -> VC + SNR report =="

$clientDir = "C:\VoiceStudio\src\Client"
$segments = "C:\VoiceStudio\projects\demo\dataset\segments"
$manifest = Join-Path $segments "manifest.json"

function Run-Job($name) {
  Write-Host "`n-- $name --"
  Push-Location $clientDir
  dotnet run -- --job (Join-Path $JobsDir $name)
  Pop-Location
}

Run-Job "convert.json"
Run-Job "vad.json"
Run-Job "asr.json"
Run-Job "align.json"
Run-Job "tts.json"
Run-Job "vc.json"

# SNR report (Agent D version may move into vad.py; this standalone is fine too)
$venv = "C:\VoiceStudio\workers\python\vsdml\.venv\Scripts\python.exe"
$snr = "C:\VoiceStudio\workers\python\vsdml\snr_report.py"
if (Test-Path $snr) {
  & $venv $snr --segments $segments | Tee-Object -Variable snrOut | Out-Host
}

Write-Host "`nAll good. Outputs under C:\VoiceStudio\projects\demo"
