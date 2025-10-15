param([Parameter(Mandatory=$true)][string]$InPath,[Parameter(Mandatory=$true)][string]$OutPath)
$ErrorActionPreference = "Stop"
if (!(Get-Command ffmpeg -ErrorAction SilentlyContinue)) { Write-Host "FFmpeg missing. Run build\install-ffmpeg.ps1"; exit 1 }
$dir = Split-Path -Parent $OutPath
New-Item -ItemType Directory -Force -Path $dir | Out-Null
& ffmpeg -y -hide_banner -loglevel error -i "$InPath" -ac 1 -ar 48000 -vn -map_metadata -1 -sample_fmt s16 "$OutPath"
if ($LASTEXITCODE -eq 0) { Write-Host "Converted -> $OutPath" } else { Write-Host "Conversion failed."; exit 1 }
