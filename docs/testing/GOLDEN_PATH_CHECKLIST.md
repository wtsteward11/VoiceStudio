# Golden Path Acceptance Test

This checklist defines "working" objectively. Every item must pass before a change is considered safe. Run after every reintegration slice, every build config change, and before every release.

## Build and XAML Health

- [ ] `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` succeeds with 0 errors
- [ ] `tools/build/Check-XamlHealth.ps1` reports PASS (XamlTypeInfo.g.cs > 1 KB)
- [ ] `tools/build/Verify-BuildConfigLock.ps1` reports PASS
- [ ] `tools/build/Verify-ResolvedPackages.ps1` reports PASS (no banned 9.0+ packages)
- [ ] `tools/build/Verify-XamlArtifacts.ps1` reports PASS

## UI Launch

- [ ] VoiceStudio.App.exe launches from `.buildlogs/x64/Debug/net8.0-windows10.0.19041.0/`
- [ ] UI stays running for 8+ seconds (not an immediate crash/exit)
- [ ] Main window renders with navigation sidebar visible

## Settings Panel

- [ ] Settings panel opens
- [ ] Engine category tab present and clickable
- [ ] Plugins category tab present and clickable
- [ ] MCP category tab present and clickable

## Backend Connectivity

- [ ] Backend starts: `.venv/Scripts/python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8001`
- [ ] Health endpoint responds: `curl http://localhost:8001/health` returns 200
- [ ] API docs load: `http://localhost:8001/docs` renders Swagger UI

## Core Feature Pipeline

- [ ] Upload/import audio file via UI or API (`POST /api/audio/upload`)
- [ ] Uploaded file appears in Library panel
- [ ] Engine list populates in Voice Synthesis panel
- [ ] Synthesize request produces output audio artifact
- [ ] Output audio plays back in the UI

## Verification Harness

- [ ] `python scripts/run_verification.py` reports Overall: PASS (5/5 checks)
- [ ] `scripts/verify.ps1 -Quick` completes without failures

## Quick Command Reference

```powershell
# Full golden path in one block
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\tools\build\Check-XamlHealth.ps1
.\tools\build\Verify-BuildConfigLock.ps1
.\tools\build\Verify-ResolvedPackages.ps1
python scripts/run_verification.py
Start-Process ".buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
```
