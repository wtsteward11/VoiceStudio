# Lifecycle Offender Queue

**Date:** 2026-03-17  
**Purpose:** Ranked list of ViewModels with lifecycle violations. Per plan Task 5.  
**Reference:** [PANEL_LIFECYCLE_AUDIT.md](PANEL_LIFECYCLE_AUDIT.md)

---

## Priority Definitions

| Priority | Description | Risk |
|----------|-------------|------|
| **P0** | Constructor subscriptions (memory leak, permanent unsubscribe risk) | High |
| **P1** | OnDeactivated does not unsubscribe/stop timers | Medium |
| **P2** | OnActivated does not resubscribe (permanent unsubscribe after first deactivate) | Medium |
| **P3** | Retained async without ownership (fire-and-forget, no CancellationTokenSource) | Low |

---

## P0 — Constructor Subscriptions

| ViewModel | Issue | Location |
|-----------|-------|----------|
| VoiceQuickCloneViewModel | `_eventAggregator?.Subscribe<CloneReferenceSelectedEvent>` in constructor. Does NOT implement IPanelLifecycle; never unsubscribes. | Line 73 |
| VoiceCloningWizardViewModel | `_eventAggregator?.Subscribe<CloneReferenceSelectedEvent>` in constructor. No OnDeactivatedAsync unsubscribe. | Line 118 |
| GPUStatusViewModel | `SetupAutoRefresh()` in constructor creates and starts Timer. OnDeactivatedAsync returns Task.CompletedTask without stopping timer. | Lines 65, 78 |
| RecordingViewModel | `_statusTimer` created in constructor. Started in OnActivatedAsync; OnDeactivatedAsync returns Task.CompletedTask. Timer may not stop on deactivate. | Lines 120–123, 167 |
| RealTimeVoiceConverterViewModel | `InitializeMonitoringTimer()` in constructor; WebSocket client init. OnDeactivatedAsync stops timer (line 469) — verify. | Constructor |
| PluginManagementViewModel | Constructor "Subscribe to bridge events" — verify if EventAggregator or plugin bridge. | Line 58 |
| UpdateViewModel | Constructor "Subscribe to update service events" | Line 34 |

---

## P1 — OnDeactivated Does Not Unsubscribe

| ViewModel | Issue |
|-----------|-------|
| VoiceQuickCloneViewModel | No IPanelLifecycle; no unsubscribe path. |
| VoiceCloningWizardViewModel | OnDeactivatedAsync not implemented or returns Task.CompletedTask without unsubscribe. |
| GPUStatusViewModel | OnDeactivatedAsync => Task.CompletedTask; does not call StopAutoRefresh(). Timer continues until Dispose. |
| ScriptEditorViewModel | OnDeactivatedAsync => Task.CompletedTask. Subscribes in OnActivatedAsync — must unsubscribe in OnDeactivatedAsync. |
| EnsembleSynthesisViewModel | OnDeactivatedAsync => Task.CompletedTask. Subscribes in OnActivatedAsync — verify unsubscribe. |
| TagManagerViewModel | OnDeactivatedAsync => Task.CompletedTask. Has "Subscribe to selection changes" in OnActivatedAsync. |
| MarkerManagerViewModel | OnDeactivatedAsync => Task.CompletedTask. Has "Subscribe to selection changes" in OnActivatedAsync. |
| MultiVoiceGeneratorViewModel | OnDeactivatedAsync => Task.CompletedTask. Subscribes in OnActivatedAsync. |
| AutomationViewModel | OnDeactivatedAsync => Task.CompletedTask. Subscribes in OnActivatedAsync. |
| APIKeyManagerViewModel | OnDeactivatedAsync => Task.CompletedTask. Subscribes in OnActivatedAsync. |
| AudioAnalysisViewModel | OnDeactivatedAsync => Task.CompletedTask. |
| DeepfakeCreatorViewModel | OnDeactivatedAsync => Task.CompletedTask. |
| VideoGenViewModel | OnDeactivatedAsync => Task.CompletedTask. |
| TextHighlightingViewModel | OnDeactivatedAsync => Task.CompletedTask. |
| SpatialStageViewModel | OnDeactivatedAsync => Task.CompletedTask. |
| MultilingualSupportViewModel | OnDeactivatedAsync => Task.CompletedTask. |
| AdvancedSpectrogramVisualizationViewModel | OnDeactivatedAsync => Task.CompletedTask. |
| MCPDashboardViewModel | OnDeactivatedAsync disposes — verify. |
| TrainingQualityVisualizationViewModel | OnDeactivatedAsync => Task.CompletedTask. |

---

## P2 — OnActivated Does Not Resubscribe

ViewModels that subscribe in OnActivatedAsync and unsubscribe in OnDeactivatedAsync are correct.  
ViewModels that subscribe once (e.g. in constructor) and never resubscribe after deactivate are P2.  
See P0 — constructor subscriptions imply P2 (after first deactivate, no resubscribe).

---

## P3 — Retained Async Without Ownership

| ViewModel | Issue |
|-----------|-------|
| JobProgressViewModel | `_ = _webSocketClient.ConnectAsync()` and `StartPolling()` in OnActivatedAsync. Verify CancellationTokenSource ownership. |
| RealTimeVoiceConverterViewModel | WebSocket ConnectAsync, monitoring timer. |
| PipelineConversationViewModel | `_streamingClient.ConnectAsync` in OnActivatedAsync. |
| LibraryViewModel | Fire-and-forget in OnActivatedAsync; has _disposalCts. Documented in TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS. |

---

## Recommended Fix Order

1. **VoiceQuickCloneViewModel** — Move Subscribe to OnActivatedAsync; add OnDeactivatedAsync unsubscribe; implement IPanelLifecycle.
2. **VoiceCloningWizardViewModel** — Move Subscribe to OnActivatedAsync; add OnDeactivatedAsync unsubscribe.
3. **GPUStatusViewModel** — Call StopAutoRefresh() in OnDeactivatedAsync; call SetupAutoRefresh() in OnActivatedAsync.
4. **ScriptEditorViewModel** — Add unsubscribe in OnDeactivatedAsync (subscribe is already in OnActivatedAsync).
5. **RecordingViewModel** — Ensure _statusTimer.Stop() in OnDeactivatedAsync.

---

## Verification

- Grep: `Subscribe|SubscribeAsync|_eventAggregator.Subscribe` in ViewModels
- Grep: `Timer|WebSocket|ConnectAsync|StartPolling` in ViewModels
- Grep: `OnDeactivatedAsync.*Task.CompletedTask` — cross-reference with ViewModels that have subscriptions

---

## Changelog

- 2026-03-17: Initial queue from scan. P0–P3 identified. Top 5 fix order recommended.
