param(
  [ValidateSet("menu","gui","split","patch_xtts","patch_speaker","align_demo")]
  [string]$App = "menu",
  [string[]]$Args
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$py = Join-Path $root ".venv\Scripts\python.exe"
if (-not (Test-Path $py)) { $py = "python" }

# load defaults
$defsPath = Join-Path $PSScriptRoot 'defaults.json'
if (-not (Test-Path $defsPath)) { throw "Missing defaults: tools\defaults.json" }
$defs = Get-Content -Raw -LiteralPath $defsPath | ConvertFrom-Json

function R([string]$rel) { if ([IO.Path]::IsPathRooted($rel)) { $rel } else { Join-Path $root $rel } }

$apps = [ordered]@{
  "gui"           = @{
                      path = "gui_offline.py"
                      desc = "Launch GUI (offline)"
                      defaultArgs = { @() }
                    }
  "split"         = @{
                      path = "split_and_transcribe.py"
                      desc = "Split + transcribe audio"
                      defaultArgs = { @((R $defs.input_audio), (R $defs.output_dir)) }
                    }
  "patch_xtts"    = @{
                      path = "patch_xtts_config.py"
                      desc = "Patch XTTS config"
                      defaultArgs = { @((R $defs.xtts_config)) }
                    }
  "patch_speaker" = @{
                      path = "patch_speaker.py"
                      desc = "Patch speaker model"
                      defaultArgs = { @((R $defs.speaker_model)) }
                    }
  "align_demo"    = @{
                      path = "workers\python\vsdml\align_whisperx.py"
                      desc = "WhisperX align demo"
                      defaultArgs = { @((R $defs.input_audio), $defs.align_language) }
                    }
}

function Resolve-AppPath($rel){
  $full = Join-Path $root $rel
  if (-not (Test-Path $full)) { throw "Missing: $rel (looked in $full)" }
  return $full
}

function Ensure-SampleWav {
  param([string]$Path)
  if (Test-Path $Path) { return }
  $code = @"
import wave, struct, math, os
p=r'$Path'; os.makedirs(os.path.dirname(p), exist_ok=True)
fr=16000; dur=1.0; f=440.0
n=int(fr*dur)
with wave.open(p,'w') as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(fr)
    for i in range(n):
        v=int(32767*0.25*math.sin(2*math.pi*f*i/fr))
        w.writeframesraw(struct.pack('<h',v))
"@
  & $py -c $code | Out-Null
}

function Run-App([string]$key, [string[]]$extraArgs){
  $cfg = $apps[$key]
  $file = Resolve-AppPath $cfg.path
  $argv = @()
  if ($extraArgs -and $extraArgs.Count) { $argv += $extraArgs } else { $argv += (& $cfg.defaultArgs) }

  if ($key -in @('split','align_demo')) {
    # make sure the sample wav exists if pointing at it
    $in = $argv | Select-Object -First 1
    if ($in -like "*sample.wav") { Ensure-SampleWav $in }
    if ($key -eq 'split' -and $argv.Count -ge 2) {
      $out = $argv[1]; if (-not (Test-Path $out)) { New-Item -ItemType Directory -Force -Path $out | Out-Null }
    }
  }

  Write-Host "==> $key : $($cfg.desc)" -ForegroundColor Cyan
  Write-Host "File: $file" -ForegroundColor DarkGray
  if ($argv.Count) { Write-Host ("Args: " + ($argv -join " ")) -ForegroundColor DarkGray }

  $env:PYTHONPATH = "$root"
  & $py $file @argv
  exit $LASTEXITCODE
}

if ($App -eq "menu") {
  Write-Host "VoiceStudio – Run App" -ForegroundColor Green
  $i = 1
  $keys = @()
  foreach($k in $apps.Keys){
    Write-Host ("[{0}] {1}  —  {2}" -f $i, $k, $apps[$k].desc)
    $keys += $k
    $i++
  }
  $sel = Read-Host "Pick a number"
  if ($sel -as [int]) {
    $idx = [int]$sel
    if ($idx -ge 1 -and $idx -le $keys.Count) {
      Run-App $keys[$idx-1] $Args
    }
  }
  Write-Host "Invalid choice." -ForegroundColor Yellow
  exit 1
}
else {
  if (-not $apps.Contains($App)) { throw "Unknown app: $App" }
  Run-App $App $Args
}
