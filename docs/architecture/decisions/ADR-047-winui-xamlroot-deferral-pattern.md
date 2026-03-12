# ADR-047: WinUI 3 XamlRoot Deferral Pattern

**Status:** Accepted
**Date:** 2026-03-10
**Decision Makers:** VoiceStudio Architecture Team
**Related:** MainWindow panel init fix (2026-03-10), ROOT_CAUSE_FIX_RULE.md, VoiceStudio Startup Crash Fix Plan

## Context

WinUI 3 requires a non-null `XamlRoot` for any control that creates a Popup, Flyout, ContentDialog, or composited visual. `XamlRoot` is only populated after the Window's content is in the live compositor tree—i.e., after the `Loaded` event fires.

During Window or Page construction, `this.Content` may be set, but `Content.XamlRoot` is **always null**. A guard condition like `rootFE.XamlRoot != null` in the constructor is dead code and will never succeed. Fire-and-forget async operations launched from the constructor that eventually create popups/dialogs will race with compositor initialization and throw `COMException` 0x8000FFFF (E_UNEXPECTED / "Catastrophic failure — XamlRoot must be explicitly set for unparented popup").

VoiceStudio experienced this as a P0 startup regression: a red error toast on every launch ("Panel initialization failed: Catastrophic failure") because `InitializePanelsAsync` was called fire-and-forget from the MainWindow constructor before the Loaded event.

## Decision

**Never fire-and-forget async operations that create WinUI controls from a Window or Page constructor.** Defer all such work to the `Loaded` event.

**Rule:** If code touches `XamlRoot`, Popup, ContentDialog, Flyout, or any compositor-hosted visual, it must run at or after `Loaded`.

## Consequences

### Positive

- Eliminates XamlRoot race conditions at startup
- Prevents "Catastrophic failure" COMException and user-facing error toasts
- Clear lifecycle contract: constructor sets up wiring; Loaded runs compositor-dependent init

### Negative

- Slightly more verbose: constructor cannot directly launch panel/overlay initialization
- Developers must remember the pattern when adding new root-level async init

### Neutral

- Constructor may still set PanelRegion bindings, subscribe events, wire handlers—anything that does not require XamlRoot
- ViewModels loading data (API calls, no XamlRoot) from constructors remain acceptable
- Child views loaded into `PanelHost` inherit XamlRoot from the parent and are unaffected

## Implementation

In MainWindow:

1. `contentFE.Loaded` handler sets `ErrorDialogService.Root = contentFE.XamlRoot` (XamlRoot guaranteed non-null)
2. `InitializePanelsAsync` is called from within the Loaded handler, not the constructor
3. Constructor sets PanelRegions and wires docking handlers only

See [MainWindow.xaml.cs](../../../src/VoiceStudio.App/MainWindow.xaml.cs) lines 305–376, 429.

## Audit Results (2026-03-10)

| Class | Fire-and-forget in constructor? | Risk |
| ----- | -------------------------------- | ---- |
| MainWindow | No (fix applied) | Resolved |
| App | `_ = Task.Run(...)` for backend startup | Safe - no WinUI controls |
| AgentApprovalDialog | None | N/A |
| FirstRunWizard | `UpdateStepUI()` triggers async only when `_currentStep` is 2 or 3; constructor sets `_currentStep = 1` | Safe - async runs after user advances, window already loaded |
| CommandPaletteWindow | None | N/A |

No additional risky patterns found in Window/Page constructors.

## References

- [docs/governance/ROOT_CAUSE_FIX_RULE.md](../../governance/ROOT_CAUSE_FIX_RULE.md)
- [docs/developer/WINUI_MIGRATION_GUIDE.md](../../developer/WINUI_MIGRATION_GUIDE.md) — WinUI 3 XamlRoot and Constructor Async section
