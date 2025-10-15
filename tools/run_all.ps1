param(
  [switch]$SkipDeps,
  [switch]$SkipSplit,
  [switch]$SkipAlign,
  [switch]$RunGui,
  [string]$Audio,
  [string]$OutDir
)

$ErrorActionPreference='Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$launcher = Join-Path $PSScriptRoot 'run_app.ps1'
if (-not (Test-Path $launcher)) { throw "Missing launcher: tools\run_app.ps1" }

# load defaults
$defsPath = Join-Path $PSScriptRoot 'defaults.json'
if (-not (Test-Path $defsPath)) { throw "Missing defaults: tools\defaults.json" }
$defs = Get-Content -Raw -LiteralPath $defsPath | ConvertFrom-Json
function R([string]$rel) { if ([IO.Path]::IsPathRooted($rel)) { $rel } else { Join-Path $root $rel } }

$audio = if ($Audio) { $Audio } else { R $defs.input_audio }
$out   = if ($OutDir) { $OutDir } else { R $defs.output_dir }

# ensure sample
function Ensure-SampleWav {
  param([string]$Path)
  if (Test-Path $Path) { return }
  $venvPy = Join-Path $root '.venv\Scripts\python.exe'
  $py = (Test-Path $venvPy) ? $venvPy : 'python'
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
Ensure-SampleWav $audio
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Force -Path $out | Out-Null }

# step 1: deps
if (-not $SkipDeps) {
  Write-Host "==> deps" -ForegroundColor Cyan
  & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'setup_python.ps1') -Dev
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# step 2: split (always pass audio/out)
if (-not $SkipSplit) {
  Write-Host "==> split" -ForegroundColor Cyan
  & pwsh -NoProfile -ExecutionPolicy Bypass -File $launcher -App split -Args @($audio, $out)
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# step 3: align demo (always pass audio + default language)
if (-not $SkipAlign) {
  Write-Host "==> align_demo" -ForegroundColor Cyan
  & pwsh -NoProfile -ExecutionPolicy Bypass -File $launcher -App align_demo -Args @($audio, $defs.align_language)
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# step 4: GUI (optional)
if ($RunGui) {
  Write-Host "==> gui" -ForegroundColor Cyan
  & pwsh -NoProfile -ExecutionPolicy Bypass -File $launcher -App gui
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "`n✅ run_all completed" -ForegroundColor Green
