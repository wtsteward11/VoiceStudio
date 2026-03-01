# VoiceStudio Developer Setup Guide

## Prerequisites

| Tool | Version | Check Command |
|------|---------|---------------|
| .NET SDK | 8.0.417+ | `dotnet --version` |
| Python | 3.11.x | `python --version` |
| Git | 2.40+ | `git --version` |
| Windows App SDK | 1.8 runtime | Installed via NuGet restore |

## 1. Clone and Configure

```powershell
git clone https://github.com/wtsteward11/VoiceStudio.git E:\VoiceStudio
cd E:\VoiceStudio
git config core.hooksPath .githooks
```

The hooks path enables the pre-commit (build config protection) and pre-push (dependency gate + XAML artifact check) hooks.

## 2. Python Virtual Environment

```powershell
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

## 3. Engine Dependencies (Optional)

Install engine-specific dependencies for voice synthesis, transcription, etc.:

```powershell
.\scripts\setup\install-engine-deps.ps1
```

## 4. GPU Setup (Optional)

For GPU-accelerated inference (CUDA):

```powershell
.\scripts\setup\setup_gpu_venv.ps1
```

## 5. Seed Data (Optional)

Populate initial data for development:

```powershell
python scripts/setup/seed_data.py
```

## 6. Build

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
```

**IMPORTANT:** Do not change `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `VoiceStudio.App.csproj`, `VoiceStudio.Core.csproj`, or `VoiceStudio.sln`. See `docs/reports/DO_NOT_CHANGE_BUILD_CONFIG.md`.

## 7. Verify

```powershell
.\scripts\verify.ps1 -Quick
```

This runs build + gate checks + XAML health in ~30 seconds.

## 8. Launch

```powershell
Start-Process ".buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
```

## 9. Start Backend (for full feature testing)

```powershell
.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8001
```

Verify: `curl http://localhost:8001/health`

## Available Setup Scripts

| Script | Purpose |
|--------|---------|
| `scripts/setup/install-engine-deps.ps1` | Install engine dependencies |
| `scripts/setup/setup_gpu_venv.ps1` | GPU/CUDA environment |
| `scripts/setup/setup_xtts_venv.ps1` | XTTS-specific environment |
| `scripts/setup/provision_venv_family.py` | Multi-venv provisioning |
| `scripts/setup/seed_data.py` | Development seed data |
| `scripts/setup/seed_data_http.py` | Seed data via HTTP API |
| `scripts/setup/setup_test_audio.ps1` | Test audio fixtures |
| `scripts/setup/install_hooks.py` | Install git hooks programmatically |
| `scripts/setup/check_installations.py` | Verify all installations |
