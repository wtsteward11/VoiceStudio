# UI Automation Spec (Phase 2 Master Plan)

> **Version**: 1.0  
> **Last Updated**: 2026-01-30  
> **Owner**: Role 3 (UI Engineer)  
> **Status**: IMPLEMENTED (v1.0.2) -- WinAppDriver integration in `tests/ui/`, CI in `.github/workflows/test.yml`  
> **Traceability**: [OPTIONAL_TASK_INVENTORY](../governance/OPTIONAL_TASK_INVENTORY.md); Phase 2 Master Plan (Technical Specification & RFCs); [ROLE3_UI_ENGINEER_COMPREHENSIVE_TASKS](../reports/verification/ROLE3_UI_ENGINEER_COMPREHENSIVE_TASKS_2026-01-29.md) §8.2

This document records the **decision and approach for UI automation** (WinAppDriver vs Playwright) for VoiceStudio's native WinUI 3 desktop app, and how it fits into Gate C / regression proof.

---

## 1. Context

- **App**: Native WinUI 3 (.NET 8) desktop, not a web app. UI automation is needed for regression testing (e.g. Gate C nav + binding checks) and optional wizard e2e.
- **Current Gate C**: [scripts/gatec-publish-launch.ps1](../../scripts/gatec-publish-launch.ps1) launches the app with `--smoke-ui` and parses output; no external UI driver.
- **Goal**: Optional automated UI tests (navigation, critical paths, wizard flow) that can run in CI or locally without requiring a human at the keyboard.

---

## 2. Options

| Option | Technology | Pros | Cons |
|--------|------------|------|------|
| **A. WinAppDriver** | Microsoft WinAppDriver (UIA2) | Native to Windows; supports WinUI/UWP/Win32. | Requires WinAppDriver server process; setup and driver compatibility with WinUI 3 can be brittle. |
| **B. Playwright** | Playwright for Windows (experimental) | Same API as web; good docs. | WinUI support is limited or experimental; may not cover full WinUI surface. |
| **C. In-process / app-hosted** | App exposes `--smoke-ui` or test endpoint | Already in use for Gate C; no extra process. | Limited to what the app logs; no full UI tree or click simulation. |
| **D. Hybrid** | Keep Gate C script (C); add WinAppDriver for optional deeper tests | Gate C remains simple; advanced automation only when needed. | Two mechanisms to maintain. |

**Decision**: **Option D (Hybrid)**. Keep the current Gate C publish + `--smoke-ui` as the primary regression proof. Add **WinAppDriver** for optional, deeper UI automation (e.g. wizard steps, panel navigation) when the team invests in it. Do not rely on Playwright for WinUI 3 until it officially supports it.

---

## 3. Approach (WinAppDriver)

### 3.1 Prerequisites

- Windows 10/11 with WinAppDriver installed (or run as standalone server).
- App under test launched with a known process name/window title so the driver can attach.
- Tests can be written in any language supported by WebDriver (e.g. Python with selenium, or C# with Appium/WebDriver).

### 3.2 Scope (Optional)

- Launch published app; attach WinAppDriver session.
- Navigate NavRail (e.g. NavProfiles, NavLibrary) and assert panels load.
- Optional: run through wizard steps (upload, configure, process) if backend is available.
- Capture results (pass/fail, screenshots on failure) for evidence.

### 3.3 Integration

- New scripts or test project under `tests/` or `scripts/` (e.g. `scripts/ui_automation_winappdriver.py` or `src/VoiceStudio.App.Tests/UI/`).
- Not required for Gate C pass; Gate C continues to use `--smoke-ui` and `ui_smoke_summary.json`.
- CI: optional job that runs WinAppDriver tests when WinAppDriver is available (e.g. self-hosted Windows runner).

### 3.4 Evidence

- Test report or proof artifact (e.g. `.buildlogs/ui_automation/`) linked from [UI_COMPLIANCE_AUDIT](../reports/verification/UI_COMPLIANCE_AUDIT_2026-01-28.md) or verification docs when runs are executed.

---

## 4. Exit Criteria (When Implemented)

- [x] WinAppDriver setup documented (installation, server start)
- [x] UI automation script or test project created
- [x] At least one automated test (e.g. nav to Profiles panel, assert panel visible)
- [x] Test execution documented (how to run, expected output)
- [x] Optional: CI job for WinAppDriver tests

---

## 5. References

- [OPTIONAL_TASK_INVENTORY](../governance/OPTIONAL_TASK_INVENTORY.md) §3.3
- [ROLE3_UI_ENGINEER_COMPREHENSIVE_TASKS](../reports/verification/ROLE3_UI_ENGINEER_COMPREHENSIVE_TASKS_2026-01-29.md) §8.2
- [scripts/gatec-publish-launch.ps1](../../scripts/gatec-publish-launch.ps1)
