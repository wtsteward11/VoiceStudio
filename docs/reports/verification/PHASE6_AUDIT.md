# Phase 6: Polish — UI, UX, Runtime Hardening

**Date**: 2026-03-07
**Plan**: Architect Hardening Plan

## 6.1 Panel Binding Audit (Manual)

**Requires**: Running the WinUI app. Execute manually.

**Checklist for 6 core panels**:
- [ ] Voice Synthesis — loads, bindings fire, no placeholder errors
- [ ] Profiles — loads, bindings fire
- [ ] Timeline — loads, bindings fire
- [ ] Effects Mixer — loads, bindings fire
- [ ] Diagnostics — loads, bindings fire
- [ ] Analyzer — loads, bindings fire

**Command to launch**: `.buildlogs\x64\Release\VoiceStudio.App\VoiceStudio.App.exe` (after build)

---

## 6.2–6.3 Startup Time Audit (Manual)

**WinUI**: `Measure-Command { Start-Process ".buildlogs\x64\Release\VoiceStudio.App\VoiceStudio.App.exe" }` — target < 3s to first paint.

**Backend**: `Measure-Command { Invoke-WebRequest -Uri http://localhost:8000/api/health -UseBasicParsing }` — target < 5s to 200.

---

## 6.4 Error Message Quality Audit (Code)

### Exception pass-throughs (potential info leak)

| File | Line | Pattern | Risk |
|------|------|---------|------|
| cloning.py | 546, 548 | `detail=str(e)` | Raw exception to client |
| synthesis.py | 563, 758, 901, 1020 | `detail=f"... {e!s}"` | Exception message to client |
| analysis.py | 303 | `detail=f"... {e!s}"` | Exception message to client |
| processing.py | 273, 375, 381, 659, 860 | `detail=f"... {e!s}"` | Exception message to client |
| testing.py | 143 | `detail=f"... {e!s}"` | Exception message to client |

**Recommendation**: For 500 errors, use generic user-facing messages and log the full exception server-side. Reserve `{e!s}` for development/debug only.

### Placeholder text (UI)

- `PlaceholderText` in XAML — acceptable (input hints)
- No "TODO" or "FIXME" in user-facing error strings found
