# Hostile Environment Test Protocol

**Purpose:** Verify VoiceStudio works on machines without development tooling.

## Target Environments

| # | Environment | What's Missing | Expected Behavior |
|---|------------|---------------|-------------------|
| 1 | Clean Windows 10 (no dev tools) | .NET SDK, Python, VS | App launches via bundled runtime |
| 2 | Clean Windows 11 (no dev tools) | .NET SDK, Python, VS | App launches via bundled runtime |
| 3 | No CUDA GPU | NVIDIA drivers, CUDA toolkit | Engines degrade to CPU gracefully |
| 4 | No cached models | HuggingFace cache, XTTS models | First-run download or offline error message |
| 5 | Low disk space | < 2 GB free | Installer warns, app handles gracefully |

## Prerequisites

- Clean Windows VM (snapshot before test)
- VoiceStudio installer EXE (from `installer/Output/`)
- No .NET SDK installed (only .NET Runtime 8.0 if bundled by installer)
- No Python installed (bundled in `installer/runtime/python/`)

## Test Steps

### Phase A: Installation

1. Copy `VoiceStudio-Setup-v1.0.0.exe` to VM
2. Run installer with logging:
   ```cmd
   VoiceStudio-Setup-v1.0.0.exe /VERYSILENT /LOG="C:\logs\install.log"
   ```
3. Verify: installer exits 0
4. Verify: Start Menu shortcut exists
5. Verify: `C:\Program Files\VoiceStudio\VoiceStudio.App.exe` exists

### Phase B: Launch

1. Launch VoiceStudio from Start Menu
2. Verify: main window appears within 10 seconds
3. Verify: navigation sidebar renders
4. Verify: Settings panel opens
5. Verify: no crash dialog or WER dump

### Phase C: Backend

1. Verify: backend auto-starts (or instructions shown in app)
2. Verify: health check returns 200 (or app shows "backend offline" message)
3. If no Python bundled: verify app shows actionable error, not a crash

### Phase D: CPU-Only Synthesis

1. Open Voice Synthesis panel
2. Enter test text: "Hello from the hostile environment test."
3. Select a CPU-capable engine (gTTS or pyttsx3)
4. Click Synthesize
5. Verify: audio output produced (or clear error if engine missing)

### Phase E: Uninstall

1. Uninstall via Add/Remove Programs
2. Verify: `C:\Program Files\VoiceStudio\` removed
3. Verify: Start Menu shortcut removed
4. Verify: no leftover files except user data in `%LOCALAPPDATA%\VoiceStudio\`

## Results Template

| Test | VM | Result | Notes |
|------|-----|--------|-------|
| A1: Install | Win10 | PASS/FAIL | |
| A1: Install | Win11 | PASS/FAIL | |
| B1: Launch | Win10 | PASS/FAIL | |
| B1: Launch | Win11 | PASS/FAIL | |
| C1: Backend | Win10 | PASS/FAIL | |
| D1: Synth | Win10 | PASS/FAIL | |
| E1: Uninstall | Win10 | PASS/FAIL | |

## Reporting

Save completed results to `docs/reports/audit/HOSTILE_ENVIRONMENT_RESULTS.md` with:
- VM specs (OS build, RAM, disk)
- Installer version and SHA256
- Full test results
- Screenshots of any failures
