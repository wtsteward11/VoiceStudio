param([string]$Remote)

$ErrorActionPreference='Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repo

# --- Identity (global + local) ---
$Name  = 'William Steward'
$Email = 'wtsteward11@gmail.com'
git config --global user.name  "$Name"  | Out-Null
git config --global user.email "$Email" | Out-Null
git config user.name  "$Name"           | Out-Null
git config user.email "$Email"          | Out-Null

# --- Init repo if needed + clear any lock ---
if (-not (Test-Path '.git')) { git init | Out-Null }
Get-Process git* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item -Force .git\index.lock -ErrorAction SilentlyContinue

# --- Ensure main branch ---
try { git branch -M main | Out-Null } catch {}

# --- Stage EVERYTHING (tracked + untracked) ---
git add . | Out-Null

# --- Commit if there’s anything to commit ---
$pending = git status --porcelain
if ($pending) {
  git commit -m "chore(repo): stage untracked files & normalize" | Out-Null
} else {
  Write-Host "Nothing to commit (working tree clean)." -ForegroundColor DarkGray
}

# --- Remote URL ---
# Guess your GitHub handle from your email; change if needed.
$GHUser = 'wtsteward11'
if (-not $Remote -or $Remote -match '<YOUR' -or $Remote -match '<you>') {
  $Remote = "https://github.com/$GHUser/VoiceStudio.git"
}

if ((git remote) -contains 'origin') { git remote set-url origin $Remote } else { git remote add origin $Remote }

Write-Host "Remote is: $(git remote get-url origin)" -ForegroundColor DarkGray

# --- Push ---
git push -u origin main
if ($LASTEXITCODE -ne 0) {
  Write-Warning "Push failed. Make sure the repo exists at: $Remote"
  Write-Host "Create it here, then rerun this script." -ForegroundColor Yellow
  exit 1
}
