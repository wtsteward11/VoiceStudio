param(
  [string]$User = "wtsteward11",
  [string]$Repo = "VoiceStudio",
  [ValidateSet("public","private","internal")]
  [string]$Visibility = "public"
)

$ErrorActionPreference='Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

# --- Git identity (global + local) ---
git config --global user.name  'William Steward'  | Out-Null
git config --global user.email 'wtsteward11@gmail.com' | Out-Null
git config user.name  'William Steward'           | Out-Null
git config user.email 'wtsteward11@gmail.com'     | Out-Null

# --- Init/Cleanup ---
if (-not (Test-Path '.git')) { git init | Out-Null }
Remove-Item -Force .git\index.lock -ErrorAction SilentlyContinue
git gc --prune=now --aggressive 2>$null

# --- Stage + commit anything pending ---
git add . | Out-Null
$pending = git status --porcelain
if ($pending) { git commit -m "chore: initial push" | Out-Null }

# --- Remote URL ---
$remoteUrl = "https://github.com/$User/$Repo.git"
if ((git remote) -contains 'origin') { git remote set-url origin $remoteUrl } else { git remote add origin $remoteUrl }
git branch -M main | Out-Null

function Ensure-GH {
  if (Get-Command gh -ErrorAction SilentlyContinue) { return }
  Write-Host "Installing GitHub CLI (gh)..." -ForegroundColor Yellow
  try { winget install --id GitHub.cli -e --source winget -h } catch {}
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    try { choco install gh -y } catch {}
  }
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI not found. Install from https://cli.github.com and rerun tools\gh_bootstrap.ps1"
  }
}

Ensure-GH

# --- Auth ---
$authed = $false
try { gh auth status 1>$null 2>$null; $authed = ($LASTEXITCODE -eq 0) } catch {}
if (-not $authed) {
  Write-Host "Opening browser for GitHub login..." -ForegroundColor Cyan
  gh auth login --hostname github.com --web --git-protocol https | Out-Null
}

# --- Create repo if missing; push ---
$exists = $false
try { gh repo view "$User/$Repo" 1>$null 2>$null; $exists = ($LASTEXITCODE -eq 0) } catch {}
if (-not $exists) {
  gh repo create "$User/$Repo" --$Visibility --source "$root" --remote origin --push --disable-wiki --confirm
} else {
  git fetch origin 2>$null
  try { git pull --rebase origin main } catch {}
  git push -u origin main
}

Write-Host "✅ Remote is: $remoteUrl" -ForegroundColor Green
