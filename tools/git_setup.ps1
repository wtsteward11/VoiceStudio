param([string]$Remote)

$ErrorActionPreference='Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repo

# --- kill stray git + clear lock ---
Get-Process git* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item -Force .git\index.lock -ErrorAction SilentlyContinue

# --- init if needed ---
if (-not (Test-Path '.git')) { git init | Out-Null }

# --- consistent line endings ---
if (-not (Test-Path '.gitattributes')) {
@"
*           text=auto
*.ps1       text eol=crlf
*.bat       text eol=crlf
*.cmd       text eol=crlf
*.cs        text eol=crlf
*.sln       text eol=crlf
*.py        text eol=lf
*.toml      text eol=lf
*.json      text eol=lf
*.yml       text eol=lf
*.yaml      text eol=lf
*.md        text eol=lf
*.txt       text eol=lf
"@ | Set-Content -LiteralPath '.gitattributes' -Encoding UTF8
}

# --- YOUR identity (global + local) ---
$desiredName  = "William Steward"
$desiredEmail = "wtsteward11@gmail.com"
git config --global user.name  "$desiredName"   | Out-Null
git config --global user.email "$desiredEmail"  | Out-Null
git config user.name  "$desiredName"            | Out-Null
git config user.email "$desiredEmail"           | Out-Null

# --- stage/commit at least once ---
git add --renormalize . | Out-Null
$hasHead = (git rev-parse --quiet --verify HEAD) 2>$null
if (-not $hasHead) { git commit --allow-empty -m "chore: initial commit" | Out-Null }
git commit -m "chore: normalize EOLs & add launchers/ci" 2>$null

# --- main branch ---
try { git branch -M main | Out-Null } catch {}

# --- remote (prompt if missing/placeholder unless -Remote supplied) ---
$origin = ''
try { $origin = (git remote get-url origin) } catch {}
$looksPlaceholder = ($origin -like '*<*>' -or $origin -match '/<YOUR-USER>/')

if ($Remote) {
  if ($origin) { git remote set-url origin $Remote | Out-Null } else { git remote add origin $Remote | Out-Null }
} elseif (-not $origin -or $looksPlaceholder) {
  $Remote = Read-Host "Enter GitHub URL (e.g. https://github.com/YourUser/VoiceStudio.git)"
  if ($Remote) {
    if ($origin) { git remote set-url origin $Remote | Out-Null } else { git remote add origin $Remote | Out-Null }
  } else {
    Write-Host "No remote set. Skipping push." -ForegroundColor Yellow
    exit 0
  }
}

# --- push ---
git push -u origin main
