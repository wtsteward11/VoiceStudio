# Stage 13 Full-Harness Blocker — 2026-03-19

## Blocker Summary

**Stage:** C# Unit Tests - Services (Stage 13)  
**Failure:** TIMED OUT after 300s (non-deterministic)  
**Latest timeout:** `artifacts/verify/20260318_235846`  
**Latest pass:** `artifacts/verify/20260318_234610` (21.5s)  
**Diagnostic:** `artifacts/verify/<run>/logs/csharp_unit_tests_shard_8_diag.txt`

## Observed Behavior

- **Run 1 (background, 20260318_210540):** Stage 13 PASSED in 12.7s
- **Run 2 (foreground, 20260318_211549):** Stage 13 TIMED OUT after 300s

Diagnostic shows vstest.console.dll polling on testhost connection; testhost does not respond. Testhost hang, not assertion failure.

## Fixes Applied (Partial)

1. **TestAppServicesHelper.EnsureInitialized()** — Always check for degraded AppServices (IEventAggregator, IViewModelContext, MultiSelectService) and re-initialize when DegradedModeIntegrationTests replaces provider. This fixed WorkflowCoordinatorServiceTests assertion failures (7 tests) when Services shard ran in isolation or when test order was favorable.

2. **ToastNotificationServiceTests** — [TestCategory("UI")] excludes from Services shard (filter `TestCategory!=UI`). Intended to avoid testhost crash/hang from XAML/StackPanel in Services shard.

## Classification Verification (2026-03-19)

**Confirmed via code and --list-tests:**
- `dotnet test ... --filter "TestCategory!=UI&...&FullyQualifiedName~VoiceStudio.App.Tests.Services" --list-tests` → ToastNotificationServiceTests and ContextMenuServiceTests **absent** from output.
- Both classes have `[TestCategory("UI")]` at class level; Services shard filter excludes them.
- **Audit:** Only ToastNotificationServiceTests and ContextMenuServiceTests use `[UITestMethod]` in Services folder; both correctly categorized.
- **Conclusion:** Classification fix applied. Stage 13 still times out — hypothesis: testhost contamination from prior shards or other Services test (non-XAML).

## Contamination Hypothesis Test (2026-03-19)

**Script:** `scripts/stage13_contamination_test.ps1` — runs Legacy (shard 7) then Services (shard 8) in sequence.

**Evidence:**
- Services in isolation: PASS (~10s, 696 passed, 1 skipped). `.\scripts\verify.ps1 -OnlyStage "C# Unit Tests - Services"`.
- Legacy in isolation: PASS (~4s, 993 passed).
- Legacy + Services sequence: Legacy PASS; Services run started but script was interrupted at 120s — inconclusive.
- Full harness: Stage 13 TIMED OUT 300s when Services runs 8th after 7 prior C# shards.

**Conclusion:** Testhost contamination hypothesis (Services after 7 shards) remains plausible. Mitigation (pre-C# cleanup, 5s delay before Services) remains necessary until root cause identified. Blame-hang output in `artifacts/verify/<run>/logs/csharp_unit_tests_shard_8_diag.txt` when timeout occurs may identify last running test.

## Remaining Failure Mode

**testhost hang** — Non-deterministic. When Services shard runs in full harness after 7 prior C# shards (ViewModels Seam A-D through Legacy), testhost can hang without completing. vstest polls for ~300s then harness kills the stage.

## Next Steps (Per Plan)

1. Do NOT update STATE or proof index (repeated green not achieved).
2. Diagnose hang: identify which test or subcluster causes testhost to stop responding when Services runs 8th in sequence.
3. Consider: increase pre-C# cleanup rigor, shard split, or further test isolation.
4. Re-run full verify only after hang root cause is addressed.

## Plan Status

Stage 13 Root-Cause Diagnosis Plan: **COMPLETE** (2026-03-19). DegradedModeIntegrationTests.TestCleanup fix applied. Two full verify runs passed Stage 13. Blocker closed.

## Stage 13 Classification Proof Wave — Implementation Summary (2026-03-19)

**Completed:**
- Task 1: STATE.md truth discipline — "Stage 13 has shown both repeated PASS and repeated TIMEOUT; currently non-deterministic"
- Task 2: ToastNotificationServiceTests and ContextMenuServiceTests verified excluded via --list-tests; no other UI offenders in Services
- Task 3: Contamination test script `scripts/stage13_contamination_test.ps1`; hypothesis remains plausible
- Task 4: No offender identified — cancelled
- Task 5: Services in isolation PASS (696 tests, ~10s)
- StartupDiagnosticsWriter.cs: empty-catch fix (Debug.WriteLine) — empty_catch_check PASS

**Blocked (Stage 13 non-deterministic):**
- Task 6: Repeated full green — run 20260318_234610 passed Stage 13; run 20260318_235846 timed out
- Tasks 7–9: Blocked by Task 6

**Next:** Continue full verify.ps1 runs; when 2–3 consecutive green achieved, proceed to truth-sync and architecture wave.

---

## Stage 13 Root-Cause Diagnosis (Task A–B, 2026-03-19)

### Subcluster Matrix (Task A)

**Script:** `scripts/stage13_subcluster_matrix.ps1`  
**Run:** 2026-03-19 00:21:54  
**Result:** All 12 runs PASSED (no hang reproduced)

| Subset       | Mode        | Preceding   | Pass | Runtime (s) |
|--------------|-------------|-------------|------|-------------|
| Services A-C | Isolation   | None        | PASS | 5           |
| Services A-C | After Legacy| Legacy      | PASS | 5           |
| Services A-C | After 1-7   | Shards 1-7  | PASS | 5           |
| Services D-G | Isolation   | None        | PASS | 2           |
| Services D-G | After Legacy| Legacy      | PASS | 2           |
| Services D-G | After 1-7   | Shards 1-7  | PASS | 2           |
| Services M-P | Isolation   | None        | PASS | 6           |
| Services M-P | After Legacy| Legacy      | PASS | 6           |
| Services M-P | After 1-7   | Shards 1-7  | PASS | 6           |
| Services R-Z | Isolation   | None        | PASS | 2           |
| Services R-Z | After Legacy| Legacy      | PASS | 2           |
| Services R-Z | After 1-7   | Shards 1-7  | PASS | 9           |

**Note:** Hang is non-deterministic. When hang occurs, inspect `artifacts/verify/<run>/logs/csharp_unit_tests_shard_8_diag.txt` and blame-hang output.

### High-Risk Candidates (Task B)

1. **DegradedModeIntegrationTests** — Replaces AppServices with minimal provider; TestCleanup does not restore. **Fix:** Call `TestAppServicesHelper.EnsureInitialized()` in TestCleanup.
2. **DispatcherQueueController** — Abandoned thread; ShutdownQueueAsync skipped to avoid crash. Unconfirmed as hang source.
3. **WorkflowCoordinatorServiceTests, RequestCoordinatorIntegrationTests, Shared AppServices** — Order-dependent; unconfirmed.
