# High-Frequency Event Throttle Policy

**Status:** Policy documented; implementation complete  
**Last Updated:** 2026-03-17  
**Related:** [PANEL_WIRING_CATALOG](PANEL_WIRING_CATALOG.md) § Throttle/Debounce Policy

## Overview

High-frequency events (playback position, timeline selection, scroll sync, selection broadcast) can cause UI lag and excessive backend calls when published unthrottled. This document defines the throttle policy and implementation status.

## Event Policies

| Event | Recommended Throttle | Mode | Publishers | Status |
|-------|----------------------|------|-------------|--------|
| `PlaybackStateChangedEvent` | 100ms | Trailing | Features/Timeline/TimelineViewModel | Policy set; ThrottledEventPublisher available |
| `TimelineSelectionChangedEvent` | 100ms | Trailing | Views/Panels/TimelineViewModel, Features/Timeline | Policy set |
| `ScrollSyncEvent` | 50–100ms | Trailing | SynchronizedScrollService | Policy set |
| `SelectionBroadcastEvent` | 50ms | Trailing | SelectionBroadcastService | Policy set |

## Implementation

### ThrottledEventPublisher

`src/VoiceStudio.App/Services/ThrottledEventPublisher.cs` provides:

- **Trailing mode**: Waits for quiet period, then fires last event (best for position/selection)
- **Leading mode**: Fires immediately, then throttles
- **LeadingAndTrailing**: Both

Usage:

```csharp
_throttledPublisher.Publish(new PlaybackStateChangedEvent(...), throttleMs: 100, mode: ThrottleMode.Trailing);
```

### Wiring Checklist

- [x] Register `ThrottledEventPublisher` in AppServices (singleton)
- [x] Inject into Features/Timeline/TimelineViewModel for PlaybackStateChangedEvent
- [x] Inject into Features/Timeline/TimelineViewModel for TimelineSelectionChangedEvent
- [x] Inject into SynchronizedScrollService for ScrollSyncEvent
- [x] Inject into SelectionBroadcastService for SelectionBroadcastEvent

### Instrumentation (Dev/Smoke)

In debug builds, `ThrottledEventPublisher.GetStats()` can log:

- TotalReceived, TotalPublished, TotalCoalesced per event type
- Throttle efficiency (percentage coalesced)

## References

- `src/VoiceStudio.App/Services/ThrottledEventPublisher.cs`
- `docs/design/PANEL_WIRING_CATALOG.md` § Throttle/Debounce Policy
