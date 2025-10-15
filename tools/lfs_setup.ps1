$ErrorActionPreference="Stop"
$here = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $here

function Ensure-GitLFS {
  $hasGit = [bool](Get-Command git -ErrorAction SilentlyContinue)
  if (-not $hasGit) { throw "git not found. Install Git for Windows and rerun." }

  git lfs version *> $null
  if ($LASTEXITCODE -eq 0) { return }

  Write-Host "Installing Git LFS..." -ForegroundColor Yellow
  $installed = $false
  if (Get-Command winget -ErrorAction SilentlyContinue) {
    try { winget install -e --id GitHub.GitLFS -h; $installed = $true } catch {}
  }
  if (-not $installed -and (Get-Command choco -ErrorAction SilentlyContinue)) {
    try { choco install git-lfs -y; $installed = $true } catch {}
  }
  if (-not $installed) { throw "Git LFS not installed. Install from https://git-lfs.com and rerun." }

  git lfs version | Write-Host
}

Ensure-GitLFS
git lfs install

# Track common model/weight file types
$patterns = @("*.onnx","*.safetensors","*.pt","*.pth","*.ckpt","*.gguf","*.npz")
foreach($p in $patterns){ git lfs track "$p" | Out-Null }

git add .gitattributes
$pending = git status --porcelain
if ($pending) { git commit -m "chore(lfs): track model file types" | Out-Null }

# Rewrite history to store those types in LFS (repo is new; force push is fine)
$include = ($patterns -join ",")
git lfs migrate import --include="$include"

git branch -M main | Out-Null
git push --force-with-lease origin main
Write-Host "✅ LFS enabled and pushed." -ForegroundColor Green
