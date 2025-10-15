param([string]$QueueIn = "C:\VoiceStudio\queue\in")
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $QueueIn | Out-Null

$jobs = @(
  "C:\VoiceStudio\planning\jobs\convert.json",
  "C:\VoiceStudio\planning\jobs\vad.json",
  "C:\VoiceStudio\planning\jobs\asr.json",
  "C:\VoiceStudio\planning\jobs\align.json",
  "C:\VoiceStudio\planning\jobs\tts.json",
  "C:\VoiceStudio\planning\jobs\vc.json"
)

$i = 0
foreach ($src in $jobs) {
  if (-not (Test-Path $src)) { Write-Host "Missing $src" -ForegroundColor Red; exit 1 }
  $i++
  $dest = Join-Path $QueueIn ("m1_step{0:D2}.job.json" -f $i)
  Copy-Item $src $dest -Force
  Write-Host "[agentB] enqueued $dest" -ForegroundColor Cyan
}
