# GAP-008 startup truth — operator visual proof (cold launch)

**Date (local):** 2026-04-25  
**Build:** Debug, x64, `VoiceStudio.sln` / `VoiceStudio.App`  
**Cold-launch context:** Fresh application window after startup; backend reached steady interactive state (operator session).

## Visually confirmed (operator)

| # | Check | Result |
|---|--------|--------|
| 1 | Bottom primary status (**`StatusBar_StatusText`**) is **not** stuck on **Starting…** — shows **Ready** (or equivalent idle-ready copy) | **PASS** |
| 2 | Top shell / transport area — **no** duplicated or stale **Starting…** placeholder pollution vs steady state | **PASS** |
| 3 | **StartupOverlay** — not blocking workspace; normal shell visible | **PASS** |
| 4 | Shell consistency — e.g. **Job: Idle**, window interactive, steady usable state | **PASS** |

## Linked product evidence

- Bounded design: [VOICESTUDIO_BOUNDED_GAP008_STARTUP_TRUTH_RECOVERY.md](../../design/VOICESTUDIO_BOUNDED_GAP008_STARTUP_TRUTH_RECOVERY.md)  
- Structural pins: `Gap008StartupTruthTests` in `src/VoiceStudio.App.Tests/Views/Gap008StartupTruthTests.cs`
- Proof policy (markdown vs binary): [VERIFICATION_PROOF_ARTIFACTS.md](../../developer/VERIFICATION_PROOF_ARTIFACTS.md)

## Coverage limits (honest scope)

This proof demonstrates **one** cold-launch visual path on **one** session. It does **not** establish:

- Repeated relaunch stability  
- Degraded-backend or reconnect-only paths  
- Restart-after-failure or crash recovery  
- Suspend / resume or multi-monitor edge cases  

Further slices or operator sessions are required to widen that matrix.

## Screenshot

If a PNG was committed alongside this file, it is named:

`PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.png` (same directory).

If no binary is present, this Markdown note remains the canonical operator record for the session above.
