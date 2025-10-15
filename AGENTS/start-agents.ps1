$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path C:\VoiceStudio\queue\in,C:\VoiceStudio\queue\out | Out-Null
Start-Process powershell -ArgumentList '-NoLogo','-ExecutionPolicy','Bypass','-File','C:\VoiceStudio\agents\agentD_worker.ps1'
Start-Sleep -Seconds 1
powershell -ExecutionPolicy Bypass -File C:\VoiceStudio\agents\agentB_enqueue.ps1
Write-Host "Agents started and pipeline enqueued. Monitor C:\VoiceStudio\queue\out for *.done.json" -ForegroundColor Green
