# GOV-VOICESTUDIO-GAP007-PANELHOST-CONTENTPROPERTY-BOUNDARY-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP007_PANELHOST_CONTENTPROPERTY_BOUNDARY_01`  
**Status:** **Closed** (2026-04-07)  
**Tracker:** [GAP-007](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** shell / XAML seam (compile-time + runtime coherence)

## Problem statement

`PanelHost` used `public static new DependencyProperty ContentProperty` and `public new UIElement? Content` to shadow `ContentControl.ContentProperty` / `Content`. That shadows the base `object`-typed `Content` with a `UIElement`-typed surface and risks ambiguous XAML/compiler behavior (Gate C).

WinUI 3 does not expose `DependencyProperty.OverrideMetadata`; the fix is **Option B**: register a distinct dependency property `HostedPanel` / `HostedPanelProperty` and bind the body `ContentPresenter` to it. Base `Content` remains the inherited `ContentControl` surface (unused for panel body).

## Frozen architecture decisions

1. **Rename, do not override:** `HostedPanelProperty` registered with `nameof(HostedPanel)`, type `UIElement`, same `OnContentChanged` callback and LRU/cache semantics as before.
2. **No `new` shadow:** `PanelHost` must not declare `ContentProperty` or shadow `Content` at the declared type.
3. **Consumers:** All code that assigned or read the former `PanelHost.Content` for the hosted panel uses `HostedPanel` instead.
4. **XAML:** `PanelHost.xaml` `ContentPresenter` uses `x:Bind HostedPanel` (OneWay).

## Acceptance contract (all required)

- [x] No `public static new` on any DP field on `PanelHost`.
- [x] No `public new` CLR property shadowing `Content` on `PanelHost` for the hosted panel (use `HostedPanel`).
- [x] Build clean (no new errors/warnings in scope).
- [x] `x:Bind HostedPanel` resolves (proven by `dotnet build` + `validate_xaml_resources.py` PASS).
- [x] Panel swap / dock / smoke behavior preserved (proven by `tests/ci`, targeted `PanelHostSeamTests`, Quick verify).
- [x] Closure matrix + proof — [closure](../reports/verification/VOICESTUDIO_GAP007_PANELHOST_CONTENTPROPERTY_BOUNDARY_LANE_CLOSURE_2026-04-07.md).

## Allowlist

`src/VoiceStudio.App/Controls/PanelHost.xaml`, `PanelHost.xaml.cs`, `MainWindow.xaml.cs`, `MainWindow.Workspaces.cs`, `MainWindow.Smoke.cs`, `Services/SearchOverlayCoordinator.cs`, `src/VoiceStudio.App.Tests/Controls/PanelHostSeamTests.cs`, execution row, closure report, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`.

## Hard OUT

MainWindow decomposition; Panel LRU/eviction redesign; shell layout redesign; startup work; new panel features.

## Rollback

Revert scoped commit(s). Restore `Content`/`ContentProperty` names only if reverting entire lane.

## Changelog

- **2026-04-07:** Row frozen; rename to `HostedPanel` / `HostedPanelProperty`.
- **2026-04-07:** Lane closed — closure report linked above; tracker **GAP-007** **Closed**.
