param(
  [string]$QueueIn  = "C:\VoiceStudio\queue\in",
  [string]$QueueOut = "C:\VoiceStudio\queue\out",
  [string]$ClientProj = "C:\VoiceStudio\src\Client\VoiceStudio.Client.csproj"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $QueueIn,$QueueOut | Out-Null

function Ensure-Core {
  try {
    $r = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:5071/" -TimeoutSec 2
    if ($r.StatusCode -eq 200) { return }
  } catch {}
  Write-Host "[agentD] starting Core..." -ForegroundColor Yellow
  powershell -ExecutionPolicy Bypass -File C:\VoiceStudio\build\start-core.ps1 -Force
  Start-Sleep -Seconds 2
}

Ensure-Core
Write-Host "[agentD] watching $QueueIn ..." -ForegroundColor Cyan

while ($true) {
  $job = Get-ChildItem $QueueIn -Filter *.job.json -File | Sort-Object LastWriteTime | Select-Object -First 1
  if (-not $job) { Start-Sleep -Milliseconds 250; continue }

  $name = $job.BaseName
  Write-Host "[agentD] running $name" -ForegroundColor Green

  $result = @{
    job      = $job.FullName
    started  = (Get-Date).ToString("s")
    status   = "unknown"
    code     = ""
    outputs  = @()
    message  = ""
  }

  Push-Location (Split-Path $ClientProj)
  try {
    $proc = Start-Process -FilePath "dotnet" -ArgumentList "run -- --job `"$($job.FullName)`"" -PassThru -NoNewWindow -RedirectStandardOutput out.txt -RedirectStandardError err.txt
    $proc.WaitForExit()
    $out = Get-Content out.txt -Raw
    $err = Get-Content err.txt -Raw
    Remove-Item out.txt, err.txt -ErrorAction SilentlyContinue

    if ($out -match "Run status:\s*(\w+), code:\s*(.*)") {
      $result.status = $Matches[1]
      $result.code   = $Matches[2].Trim()
    } else {
      $result.status = "error"
      $result.code   = "E_PARSE"
    }
    $result.outputs = ($out -split "`r?`n" | Where-Object {$_ -like "Output:*"} | ForEach-Object { ($_ -split "Output:\s*")[1].Trim() })
    $result.message = ($err + "`n" + $out).Trim()
  }
  catch {
    $result.status = "error"
    $result.code   = "E_EXCEPTION"
    $result.message= $_.Exception.Message
  }
  finally {
    Pop-Location
  }

  $result.finished = (Get-Date).ToString("s")
  $done = Join-Path $QueueOut ($name + ".done.json")
  ($result | ConvertTo-Json -Depth 6) | Set-Content -Encoding UTF8 -Path $done

  Move-Item $job.FullName (Join-Path $QueueOut ($name + ".job.json")) -Force
  Write-Host "[agentD] finished $name → $done" -ForegroundColor Green
}
