# VoiceStudio Quickstart (Fresh Clone Build)

Build VoiceStudio from a clean clone with zero cached state.

## Prerequisites

- Windows 10/11 (x64)
- .NET SDK 8.0.417+ (`winget install Microsoft.DotNet.SDK.8`)
- Git (`winget install Git.Git`)
- Python 3.9+ (for backend; optional for C# build only)

## Clone and Build

```powershell
git clone https://github.com/wtsteward11/VoiceStudio.git
cd VoiceStudio
dotnet restore VoiceStudio.sln
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
```

Expected: `Build succeeded. 0 Error(s)` in under 2 minutes.

## Launch UI

```powershell
Start-Process ".buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
```

Expected: VoiceStudio main window with navigation sidebar.

## Start Backend (optional)

```powershell
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8001
```

Verify: `curl http://localhost:8001/health` returns 200.

## Run Verification

```powershell
python scripts/run_verification.py
```

Expected: `Overall: PASS` (5/5 checks).

## Build Config Protection

See `docs/reports/DO_NOT_CHANGE_BUILD_CONFIG.md` for the complete list of protected files and values. Do not modify any of them.
