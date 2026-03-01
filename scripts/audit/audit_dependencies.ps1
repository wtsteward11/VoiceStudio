param(
    [switch]$SkipDotnet,
    [switch]$SkipPip
)

Write-Host "=== VoiceStudio Dependency Audit ===" -ForegroundColor Cyan

if (-not $SkipDotnet) {
    Write-Host "\n[.NET] Checking NuGet vulnerabilities..." -ForegroundColor Yellow
    try {
        dotnet list .\src\VoiceStudio.App\VoiceStudio.App.csproj package --vulnerable
    }
    catch {
        Write-Warning "dotnet list package failed: $_"
    }
}

if (-not $SkipPip) {
    Write-Host "\n[Python] Checking pip vulnerabilities..." -ForegroundColor Yellow
    try {
        if (Get-Command pip-audit -ErrorAction SilentlyContinue) {
            pip-audit
        }
        elseif (Get-Command safety -ErrorAction SilentlyContinue) {
            safety check
        }
        else {
            Write-Warning "Install pip-audit (preferred) or safety to run Python audit."
        }
    }
    catch {
        Write-Warning "Python audit failed: $_"
    }
}

Write-Host "\nAudit complete." -ForegroundColor Green
