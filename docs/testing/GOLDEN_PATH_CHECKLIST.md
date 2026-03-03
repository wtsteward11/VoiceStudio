# Golden Path Acceptance Test

This checklist defines "working" objectively. Every item must pass before a change is considered safe. Run after every reintegration slice, every build config change, and before every release.

## 1. Build and XAML Health

- [ ] `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` succeeds with 0 errors
- [ ] `tools/build/Check-XamlHealth.ps1` reports PASS (XamlTypeInfo.g.cs > 1 KB)
- [ ] `tools/build/Verify-BuildConfigLock.ps1` reports PASS
- [ ] `tools/build/Verify-ResolvedPackages.ps1` reports PASS (no banned 9.0+ packages)
- [ ] `tools/build/Verify-XamlArtifacts.ps1` reports PASS

**Pass criteria:** All 5 checks exit 0.
**Time budget:** < 2 minutes.

## 2. UI Launch

- [ ] VoiceStudio.App.exe launches from `.buildlogs/x64/Debug/net8.0-windows10.0.19041.0/`
- [ ] UI stays running for 8+ seconds (not an immediate crash/exit)
- [ ] Main window renders with navigation sidebar visible

**Pass criteria:** Process alive after 8 seconds, main window visible.
**Time budget:** < 15 seconds.

## 3. Settings Panel

- [ ] Settings panel opens
- [ ] Engine category tab present and clickable
- [ ] Plugins category tab present and clickable
- [ ] MCP category tab present and clickable

**Pass criteria:** All tabs render without crash.

## 4. Backend Connectivity

- [ ] Backend starts: `.venv/Scripts/python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8001`
- [ ] Health endpoint responds: `curl http://localhost:8001/health` returns 200
- [ ] API docs load: `http://localhost:8001/docs` renders Swagger UI

**Pass criteria:** Health 200, Swagger renders.
**Time budget:** < 30 seconds from backend start.

## 5. Core Feature Pipeline (E2E)

**Test fixture:** `tests/assets/canonical/standard/allan_watts_15s.wav` (LFS-tracked, 15 seconds)

- [ ] Upload/import audio file via API: `POST /api/audio/upload` with fixture file
- [ ] Upload returns 200/201 with `asset_id`
- [ ] Asset appears in Library panel (or `GET /api/library/assets` includes it)
- [ ] Transcription: `POST /api/transcribe/` with `engine=whisper` returns transcription text
- [ ] Synthesis: `POST /api/voice/synthesize` with sample text returns `audio_id`
- [ ] Output audio artifact exists on disk (non-zero size)
- [ ] Quality metrics visible via `GET /api/quality/metrics` or Quality Dashboard panel

**Pass criteria:** All 7 steps produce expected results.
**Time budget:** < 60 seconds total (CPU-only synthesis).
**Expected output:** Audio file > 1 KB in the output directory.

## 6. Verification Harness

- [ ] `python scripts/run_verification.py` reports Overall: PASS (5/5 checks)
- [ ] `scripts/verify.ps1 -Quick` completes without failures (when parse fixes land)

**Pass criteria:** Overall PASS.

## 7. Release Build (Gate C)

- [ ] `dotnet build VoiceStudio.sln -c Release -p:Platform=x64` succeeds with 0 errors
- [ ] `scripts/gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -NoLaunch` exits 0
- [ ] Published EXE launches and stays running for 8+ seconds

**Pass criteria:** Release build 0 errors, publish sanity PASS, EXE alive.

## Quick Command Reference

```powershell
# Full golden path in one block
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\tools\build\Check-XamlHealth.ps1
.\tools\build\Verify-BuildConfigLock.ps1
.\tools\build\Verify-ResolvedPackages.ps1
python scripts/run_verification.py
Start-Process ".buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"

# Release gate
dotnet build VoiceStudio.sln -c Release -p:Platform=x64
.\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -NoLaunch
```

## Real Mode Golden Path (SSOT Proof)

Runs the **real** golden path E2E test (with actual engines) and generates a PROOF_GOLDEN_PATH_REAL_*.json SSOT proof.

**Prerequisites:**
- Backend running on `http://localhost:8000`
- `whisper_cpp` GGUF model installed
- `piper` or `xtts` model installed
- Check: `python scripts/golden_path_preconditions.py --check-backend http://localhost:8000 --json`

**Command:**
```powershell
.\scripts\dev\run_golden_path_real.ps1
```

**Expected output:**
- `PROOF_GOLDEN_PATH_REAL_YYYY-MM-DD.json` in `docs/reports/verification/`

**To commit the proof:**
```powershell
git add docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json
git commit -m "ci(proof): add real-mode golden path proof"
```

**Note:** Real mode is a developer-machine gate. GitHub Actions runners do not have models. A self-hosted runner with preinstalled models is required for CI real-mode enforcement.
