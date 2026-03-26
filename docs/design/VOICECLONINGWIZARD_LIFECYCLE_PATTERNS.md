# VoiceCloningWizardViewModel Lifecycle Async Patterns

**Date:** 2026-03-13  
**Purpose:** Document fire-and-forget lifecycle patterns in VoiceCloningWizardViewModel. Seam-migrated (IVoiceCloningWizardClient).  
**Related:** [SEAM_MATURITY_AUDIT.md](SEAM_MATURITY_AUDIT.md), [BATCH_PROCESSING_LIFECYCLE_PATTERNS.md](BATCH_PROCESSING_LIFECYCLE_PATTERNS.md)

---

## Summary

VoiceCloningWizardViewModel uses `_disposalCts` for cancellation of fire-and-forget operations. InitializeAsync (called from Loaded) triggers LoadEnginesAsync with _disposalCts.Token so disposal cancels in-flight load.

---

## Fire-and-Forget Paths

| Trigger | Method | Cancellation | Status |
|---------|--------|--------------|--------|
| InitializeAsync (Loaded) | LoadEnginesAsync | _disposalCts.Token | Gated |

---

## Cancellation Ownership

- `_disposalCts`: Cancelled when the ViewModel is disposed. LoadEnginesAsync uses it; disposal cancels in-flight load.

---

## Changelog

- 2026-03-13: Initial document. Added _disposalCts; LoadEnginesAsync now uses it (was 30s timeout token). No silent catches.
