# Cross-Panel Smoke Verification

> **Purpose:** Manual verification checklist for cross-panel navigation and data flow after seam migrations.  
> **Use when:** Post-migration smoke, release prep, or regression investigation.  
> **Last Updated:** 2026-03-14

---

## Scope

After ViewModel seam migrations (IBackendClient → domain clients), cross-panel flows that share data via EventAggregator, ContextManager, or shared services must still work. This doc captures manual verification steps.

---

## Prerequisites

- Backend running (or stub mode if applicable)
- App built and launched: `VoiceStudio.App.exe` (Debug or Release)
- No blocking errors in logs

---

## Verification Steps

### 1. Panel Load (No Crash)

| Panel | Action | Expected |
|-------|--------|----------|
| Quality Control | Open Quality Control panel | Loads; no crash; presets/analysis UI visible |
| Quality Dashboard | Open Quality Dashboard panel | Loads; overview/presets/trends visible |
| Quality Benchmark | Open Quality Benchmarking panel | Loads; profile list, test text, engine toggles |
| Quality Optimization Wizard | Open Quality Optimization Wizard | Loads; step 1; profile selector |
| Real-Time Voice Converter | Open Real-Time Voice Converter | Loads; sessions list; profile selectors |

### 2. Cross-Panel Navigation

| Flow | Steps | Expected |
|------|-------|----------|
| Profiles → Synthesis | Select profile in Profiles; open Synthesis panel | Selected profile available in Synthesis |
| Library → Timeline | Add asset; open Timeline | Asset visible in timeline |
| Synthesis → Timeline | Synthesize; add to timeline | Clip appears in timeline |
| Training → Profiles | Create profile in Training; open Profiles | New profile listed |

### 3. Seam-Migrated Panels (Post-Migration Smoke)

For panels migrated to domain clients (e.g. IQualityControlClient, IRealTimeVoiceConverterClient):

| Panel | Client | Smoke Check |
|-------|--------|-------------|
| Quality Control | IQualityControlClient | Load presets; run analysis (if backend available) |
| Quality Dashboard | IQualityControlClient | Load overview; load presets; load trends |
| Quality Benchmark | IQualityControlClient, IProfilesClient | Select profile; run benchmark |
| Quality Optimization Wizard | IVoiceSynthesisService, IQualityControlClient, IProfilesClient | Load profiles; analyze quality |
| Real-Time Voice Converter | IRealTimeVoiceConverterClient, IProfilesClient | Load sessions; load profiles |

### 4. Gate C UI Smoke (Automated)

For deterministic UI smoke (no manual steps):

```powershell
.\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke
```

**Expected:** Exit 0; `ui_smoke_summary.json` shows `exit_code: 0`, `binding_failure_count: 0`.

---

## Result Log Template

| Date | Tester | Result | Notes |
|------|--------|--------|-------|
| YYYY-MM-DD | — | PASS / FAIL | — |

---

## Related

- [UI_TESTING_GUIDE.md](../developer/UI_TESTING_GUIDE.md) — AutomationId, WinAppDriver
- [AUTOMATION_ID_REGISTRY.md](../developer/AUTOMATION_ID_REGISTRY.md) — Stable IDs for automation
- [NEXT_10_TASKS_PLAN_V2.md](NEXT_10_TASKS_PLAN_V2.md) — Task 4/7 manual smoke references
