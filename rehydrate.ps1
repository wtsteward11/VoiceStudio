param([switch])

function Write-Ok([string]){ Write-Host ""✅ "" -ForegroundColor Green }
function Write-Warn([string]){ Write-Host ""⚠️  "" -ForegroundColor Yellow }
function Write-Err([string]){ Write-Host ""❌ "" -ForegroundColor Red }

C:\VoiceStudio = ""C:\VoiceStudio""
if (!(Test-Path C:\VoiceStudio)){ Write-Err ""Project folder not found: C:\VoiceStudio""; exit 1 }

Write-Ok ""Found repo at C:\VoiceStudio""

# .NET
try {  = (& dotnet --version); Write-Ok "".NET SDK: "" } catch { Write-Err "".NET SDK missing (winget install Microsoft.DotNet.SDK.8)"" }

# FFmpeg
try {  = (& ffmpeg -version | Select-String -Pattern '^ffmpeg version').Line; if (){ Write-Ok ""FFmpeg: "" } else { throw } } catch { Write-Err ""FFmpeg missing (winget install Gyan.FFmpeg)"" }

# Piper + voice
 = Join-Path C:\VoiceStudio ""tools\piper""
 = Join-Path  ""piper.exe""
 = Join-Path  ""voices\en_US-amy-low.onnx""
 = Join-Path  ""voices\en_US-amy-low.onnx.json""
if (Test-Path ) { Write-Ok ""Piper: "" } else { Write-Err ""Missing "" }
if ((Test-Path ) -and (Test-Path )) { Write-Ok ""Piper voice OK: en_US-amy-low"" } else { Write-Err ""Piper voice files missing"" }

# Worker venv libs
 = Join-Path C:\VoiceStudio ""workers\python\vsdml\.venv\Scripts\python.exe""
if (Test-Path ) {
  Write-Ok ""Worker venv Python: ""
   = @'
import json, importlib
mods = ["torch","torchaudio","faster_whisper","whisperx"]
out={}
for m in mods:
    try:
        mod = importlib.import_module(m)
        out[m] = getattr(mod, "__version__", "?")
    except Exception as e:
        out[m] = "ERROR:" + type(e).__name__
print(json.dumps(out))
'@
   = &  -c 
  try {
     =  | ConvertFrom-Json
     = .PSObject.Properties | ForEach-Object { ""="" }
    Write-Ok (""Worker libs: "" + ( -join "", ""))
  } catch {
    Write-Warn ""Could not parse worker library versions. Raw: ""
  }
} else {
  Write-Err ""Worker venv not found. Create it:
  cd C:\VoiceStudio\workers\python\vsdml
  python -m venv .venv
  . .\.venv\Scripts\Activate.ps1
  pip install -r requirements_m3.txt""
}

# Core health
try {
   = Invoke-WebRequest -UseBasicParsing -Uri ""http://127.0.0.1:5071/"" -TimeoutSec 3
  if (.StatusCode -eq 200 -and .Content -like ""*VoiceStudio Core gRPC*"") {
    Write-Ok ""Core reachable on http://127.0.0.1:5071""
  } else { throw }
} catch {
  Write-Warn ""Core not responding. Start it:
  cd C:\VoiceStudio\src\Core
  dotnet run""
}

# Optional client smoke
if () {
  if ( -and .StatusCode -eq 200) {
    Write-Ok ""Running Client smoke…""
    Push-Location (Join-Path C:\VoiceStudio ""src\Client"")
    try { & dotnet run -- --job (Join-Path C:\VoiceStudio ""planning\jobs\tts.json"") } finally { Pop-Location }
  } else {
    Write-Warn ""Skipping client smoke; Core is not up.""
  }
}

Write-Ok ""Rehydrate checks finished.""
