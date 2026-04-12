# GOV-VOICESTUDIO-GAP067-WCAG-06 Execution Row

**Status:** Closed
**Lane:** GAP-067 Slice 6 -- WCAG 2.1 AA Bounded Shell Pass
**Scope:** 5 progressive-disclosure surfaces from Slice 5
**Predecessor:** GAP-067 Slice 5 (Progressive Disclosure Authority) -- Closed 2026-04-12

---

## Hard IN

- Accessible naming (`AutomationProperties.Name`) for all icon-only and disclosure triggers
- `LabeledBy` wiring for unlabeled Sliders (following SpectrogramView.xaml pattern)
- Expander/flyout state semantics (programmatic name on Expander elements)
- AutomationId additions for controls that entered disclosure surfaces but lack IDs
- Keyboard reachability verification and documentation
- Focus order documentation
- Bounded contract tests
- Registry, tracker, STATE sync

## Hard OUT

- Full application WCAG certification
- Keyboard accelerators / AccessKey mnemonics
- Theme/token refactor
- Backend changes
- Panel redesigns
- Disclosure layout changes

---

## Acceptance Matrix

| Surface | Trigger has Name | Trigger keyboard reachable | Open/toggle by keyboard | Focus lands sensibly | Focus returns on dismiss | State exposed | Primary commands pointer-free |
| --- | --- | --- | --- | --- | --- | --- | --- |
| StatusBar SystemMetrics | [x] `AutomationProperties.Name="System metrics"` | [x] Button = Tab+Space | [x] Flyout built-in | [x] First flyout child | [x] WinUI default | N/A (flyout) | [x] yes |
| Toolbar PerfOverflow | [x] `AutomationProperties.Name="Performance metrics"` | [x] Button = Tab+Space | [x] Flyout built-in | [x] First flyout child | [x] WinUI default | N/A (flyout) | [x] yes |
| VoiceSynthesis Expander | [x] `AutomationProperties.Name="Advanced synthesis controls"` | [x] Expander built-in | [x] Expander Space/Enter | [x] Header or first child | N/A (inline) | [x] `IsExpanded` bound | [x] yes |
| Timeline TransportMore | [x] `AutomationProperties.Name="More transport options"` | [x] Button = Tab+Space | [x] Flyout built-in | [x] First flyout child | [x] WinUI default | N/A (flyout) | [x] yes |
| Transcribe Expander | [x] `AutomationProperties.Name="Advanced transcription options"` | [x] Expander built-in | [x] Expander Space/Enter | [x] Header or first child | N/A (inline) | [x] `IsExpanded` bound | [x] yes |

---

## Keyboard Reachability Verification

WinUI 3 provides built-in keyboard support for all control types used:

- **Button + Flyout:** Tab to focus, Space/Enter to open flyout, Escape to dismiss, focus returns to trigger
- **Expander:** Tab to focus header, Space/Enter to toggle, content joins tab order when expanded
- **ToggleSwitch:** Tab to focus, Space to toggle
- **Slider:** Tab to focus, Arrow keys to adjust
- **CheckBox:** Tab to focus, Space to toggle

All controls follow logical tab order within their parent layout. WinUI Flyout dismiss returns focus to the trigger button by default. Expander expand keeps focus on header; child content is reachable via subsequent Tab. Collapsed elements are removed from the automation tree.

No focus traps, no skipped controls, no custom keyboard handling required.

---

## Controls Modified

| Control | File | Change |
| --- | --- | --- |
| StatusBar_SystemMetricsButton | MainWindow.xaml | Added `AutomationProperties.Name="System metrics"` |
| CollaboratorsToggleButton | MainWindow.xaml | Added `AutomationProperties.Name="Collaborators"`, `AutomationProperties.AutomationId="MainWindow_StatusBar_CollaboratorsButton"` |
| ToolbarPerformanceOverflowButton | CustomizableToolbar.xaml | Added `AutomationProperties.Name="Performance metrics"` |
| VoiceSynthesisView_AdvancedControlsExpander | VoiceSynthesisView.xaml | Added `AutomationProperties.Name="Advanced synthesis controls"` |
| Speed Slider | VoiceSynthesisView.xaml | Added `AutomationProperties.LabeledBy="{x:Bind SpeedLabel}"` |
| Pitch Slider | VoiceSynthesisView.xaml | Added `AutomationProperties.LabeledBy="{x:Bind PitchShiftLabel}"` |
| Stability Slider | VoiceSynthesisView.xaml | Added `AutomationProperties.LabeledBy="{x:Bind StabilityLabel}"` |
| Clarity Slider | VoiceSynthesisView.xaml | Added `AutomationProperties.LabeledBy="{x:Bind ClarityLabel}"` |
| Temperature Slider | VoiceSynthesisView.xaml | Added `AutomationProperties.LabeledBy="{x:Bind TemperatureLabel}"` |
| Play button | TimelineView.xaml | Added `AutomationProperties.Name="Play"`, `AutomationProperties.AutomationId="TimelineView_PlayButton"` |
| Stop button | TimelineView.xaml | Added `AutomationProperties.Name="Stop"`, `AutomationProperties.AutomationId="TimelineView_StopButton"` |
| TransportMoreButton | TimelineView.xaml | Added `AutomationProperties.Name="More transport options"` |
| AddTrackButton | TimelineView.xaml | Added `AutomationProperties.Name="Add track"` |
| Open Recording button | TimelineView.xaml | Added `AutomationProperties.AutomationId="TimelineView_OpenRecordingButton"`, `AutomationProperties.Name="Open Recording"` |
| Loop ToggleSwitch | TimelineView.xaml | Added `AutomationProperties.AutomationId="TimelineView_LoopToggle"`, `AutomationProperties.Name="Loop playback"` |
| Zoom in button | TimelineView.xaml | Added `AutomationProperties.AutomationId="TimelineView_ZoomInButton"`, `AutomationProperties.Name="Zoom in"` |
| Zoom out button | TimelineView.xaml | Added `AutomationProperties.AutomationId="TimelineView_ZoomOutButton"`, `AutomationProperties.Name="Zoom out"` |
| TranscribeView_AdvancedOptionsExpander | TranscribeView.xaml | Added `AutomationProperties.Name="Advanced transcription options"` |

---

## New AutomationIds

| AutomationId | Type | Description |
| --- | --- | --- |
| `MainWindow_StatusBar_CollaboratorsButton` | Button | Collaborators toggle inside system metrics flyout |
| `TimelineView_PlayButton` | Button | Play transport button |
| `TimelineView_StopButton` | Button | Stop transport button |
| `TimelineView_OpenRecordingButton` | Button | Open Recording inside transport flyout |
| `TimelineView_LoopToggle` | ToggleSwitch | Loop playback inside transport flyout |
| `TimelineView_ZoomInButton` | Button | Zoom in inside transport flyout |
| `TimelineView_ZoomOutButton` | Button | Zoom out inside transport flyout |

---

## Tests

- `Gap067Slice6Tests.cs` -- 8 source-contract tests asserting accessible naming and AutomationId presence
- No new seam tests -- Slice 5's 3 seam tests already cover expander toggle and loop reachability

---

## Closure

- **Closure report:** [VOICESTUDIO_GAP067_WCAG_LANE_CLOSURE_2026-04-12.md](../../docs/reports/verification/VOICESTUDIO_GAP067_WCAG_LANE_CLOSURE_2026-04-12.md)
