# Release-Trust Hardening Wave — Closure Note

**Date:** 2026-03-16  
**Plan:** Release-Trust Hardening Wave (12 tasks)  
**Status:** Wave Complete (project release-ready: see below)

---

## Wave Complete vs Project Release-Ready

**Hardening wave complete:** All 12 planned tasks are done. Testhost leak root cause identified and two clusters fixed. Playback contract normalized. Proof artifact exists. Verification reports `stale_process_cleaned`.

**Project still has remaining release-trust items:**

- **STT/proof regeneration:** CLOSED. Proof regenerated 2026-03-16. Backend .venv has faster-whisper; write_golden_path_real_proof.py succeeded.
- **Clean no-taskkill confidence:** testhost can still linger after tests; `taskkill` remains a safety net. Build succeeds when verification runs cleanup before build_smoke. Not yet proven that test→build works without forced cleanup.
- **Final release verification:** Run full `verify.ps1` (including Release XAML smoke) before tagging. Use `python scripts/run_verification.py --build`; if `stale_process_cleaned: true` appears, teardown debt remains.

---

## Summary

The Release-Trust Hardening Wave addressed testhost teardown correctness, Library lifecycle and playback reliability, proof integrity, and release-trust closure. All 12 tasks are complete.

---

## What Was Fixed

### Phase 1: Testhost Teardown

| Task | Fix |
|------|-----|
| **Task 1** | Root cause identified: `VoiceBrowserViewModelTests` and `JobProgressViewModelTests` created `DispatcherQueueController` inline without shutdown. Documented in [TESTHOST_LEAK_FINDINGS.md](TESTHOST_LEAK_FINDINGS.md). |
| **Task 2** | Both test classes switched to `TestAppServicesHelper.EnsureInitialized()` + `AppServices.GetService<IViewModelContext>()`. `TestAssemblySetup.AssemblyCleanup` already calls `TestAppServicesHelper.Cleanup()`. |
| **Task 3** | `run_verification.py` now detects and reports `stale_process_cleaned` when testhost is killed before build. Enables trending improvement over time. |

### Phase 2: Library and Playback

| Task | Fix |
|------|-----|
| **Task 4** | `MainWindow.Smoke.cs`: Added `RunRepeatedLibraryImportPlaybackAsync()` — import A, play; import B, play; navigate away and back; play A; refresh; play B. New step `LibraryImportPlaybackRepeated` (45s timeout). |
| **Task 5** | `LibraryViewModelSeamTests.cs`: Added `OnDeactivatedAsync_Unsubscribes_NoRefreshOnEventAfterDeactivate`, `SearchAssetsAsync_StaleResult_NotApplied_WhenFolderChangedDuringSearch`, `SearchAssetsAsync_StaleResult_NotApplied_WhenAssetTypeChangedDuringSearch`. |
| **Task 6** | Verified `LibraryViewModel` uses ref-counted `IncrementLoading()`/`DecrementLoading()` consistently. No changes. |
| **Task 7** | Normalized playback contract: `LibraryAsset.audio_id` (backend + C#). `GetPlaybackAudioId` prefers `asset.AudioId` over `metadata["upload_id"]`. |

### Phase 3: Proof Integrity

| Task | Fix |
|------|-----|
| **Task 8** | [GOLDEN_PATH_PROOF_STATUS.md](GOLDEN_PATH_PROOF_STATUS.md) updated: LibraryImportPlaybackRepeated, first-class audio_id, imported-asset proof point. |
| **Task 9** | Proof artifact `PROOF_GOLDEN_PATH_REAL_2026-03-15.json` exists. STT: backend_default; TTS: espeak_ng. Regeneration requires `pip install faster-whisper==1.0.3` in backend env when needed. |
| **Task 10** | Roadmap Gap 7 marked RESOLVED; Phase E updated to "SCAFFOLDING + REAL PROOF COMPLETE". One truthful definition of "real golden path" across roadmap, proof-status doc, and proof artifact. |

### Phase 4: Release Gate

| Task | Fix |
|------|-----|
| **Task 11** | Full `verify.ps1` executed. Stages 1–5 passed (Clean Build, XAML Health, Resolved Packages, **Release XAML Smoke**, Python Quality). If testhost lingers, run `taskkill /F /IM testhost.exe` before verify. |
| **Task 12** | This closure note. |

---

## What Is Proven

- **Smoke coverage:** LibraryImportPlaybackRepeated proves repeated import/play cycles.
- **Proof artifact:** PROOF_GOLDEN_PATH_REAL_2026-03-15.json with stt_engine_name, tts_engine_name, model hashes.
- **Lifecycle tests:** LibraryViewModelSeamTests enforces OnDeactivated unsubscribe, folder/type/query staleness guards.
- **Playback contract:** First-class audio_id on LibraryAsset; backend populates from metadata.upload_id.

---

## What Remains Release-Trust Work (Pre-v1.2)

- **verify.ps1 before release:** Run full verify (no -Quick) before tagging. Kill lingering testhost if build fails with MSB3027.
- **stale_process_cleaned reporting:** `run_verification.py` now prints `[AUDIT] stale_process_cleaned: true` in console and includes it in JSON when testhost was killed. Enables trending.
- **STT for proof regeneration:** CLOSED. Proof regenerated 2026-03-16.
- **Clean no-taskkill confidence:** Not yet proven. testhost can linger; taskkill remains safety net.
- **Retained-async staleness guards:** Top 5 ViewModels — risk assessed; no release blockers.

---

## What Is Truly Deferred to v1.2

- Skip debt cleanup (SKIP_DEBT_CLEANUP_SUBPLAN.md)
- Workflow consolidation (DEFERRED_V1_2.md)
- ADR-051 TrainingViewModel FAF — decision made, retained

---

## Key Files

| Area | Files |
|------|-------|
| Verification | `scripts/run_verification.py` (stale_process_cleaned) |
| Test fixtures | `src/VoiceStudio.App.Tests/Fixtures/TestAppServicesHelper.cs` |
| Library lifecycle | `LibraryViewModel.cs`, `LibraryViewModelSeamTests.cs` |
| Smoke | `MainWindow.Smoke.cs` (RunRepeatedLibraryImportPlaybackAsync) |
| Proof | `GOLDEN_PATH_PROOF_STATUS.md`, `PROOF_GOLDEN_PATH_REAL_2026-03-15.json` |
| Full verify | `scripts/verify.ps1` |

---

## Final Release-Trust Closure (2026-03-16)

| Item | Status |
|------|--------|
| STT/proof blocker | Closed. Proof regenerated. |
| Clean no-taskkill confidence | Not proven. taskkill remains safety net. |
| Project release-ready | One accepted caveat: teardown may require taskkill before build. |

## Changelog

- 2026-03-16: Final closure. STT/proof closed; proof regenerated. Clean no-taskkill not proven; taskkill safety net documented. Verification passes on committed tree.
- 2026-03-16: Added "Wave Complete vs Project Release-Ready" section. Clarified remaining items: STT/proof regeneration, clean no-taskkill confidence, final release verification. STATE.md truth-synced. run_verification.py reports stale_process_cleaned in console + JSON.
- 2026-03-16: Initial closure note. All 12 tasks complete.
