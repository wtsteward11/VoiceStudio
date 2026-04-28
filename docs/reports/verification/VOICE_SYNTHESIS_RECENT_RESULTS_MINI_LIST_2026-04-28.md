# Voice Synthesis Recent Results Mini-List — Verification Report

**Date:** 2026-04-28  
**Scope:** In-memory **last 5** successful synthesis outputs on the Voice Synthesis panel; **Use** restores active `LastSynthesized*` state; **no** disk persistence; **no** backend/API changes.

## Non-claims (explicit)

- **Not** GAP-008 / **not** `MainWindow*ShellBridge` / **not** bounded Slice 46+ work.
- **Not** RHVoice; **no** [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md) edits in this change.
- **Not** a runtime “FULL PASS” or in-app human attestation; automated build + unit tests + `run_verification.py` + `verify.ps1 -Quick` only.

## Files changed

| File | Change |
|------|--------|
| `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` | `VoiceSynthesisRecentResult` model; `RecentSynthesisResults` / selection; `RestoreRecentResultCommand`; add/restore helpers; call sites (normal + ensemble). |
| `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml` | New grid row; recent-results `ListView` + **Use** button; InfoBar row shifts; overlay `RowSpan`. |
| `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` | 10 unit tests in `#region Recent results tests`. |
| `docs/developer/AUTOMATION_ID_REGISTRY.md` | Three automation IDs for panel, list, restore button. |

## Behavior (summary)

- On successful synthesis (and ensemble completion with audio id), a `VoiceSynthesisRecentResult` is **prepended**; collection trimmed to **5** (oldest removed).
- **Use** runs `RestoreRecentResultCommand` with the item: sets last audio id/url/duration, `WorkflowState = AudioReady`, clears playback error, refreshes result state, notifies play-related commands.
- `RelayCommand<VoiceSynthesisRecentResult>` avoids `DataTemplate` / `ElementName` binding issues.

## Verification commands (recorded)

```powershell
dotnet test VoiceStudio.sln -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis"
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
python scripts/run_verification.py
.\scripts\verify.ps1 -Quick
```

- **`dotnet test` … `VoiceSynthesis`:** **122** passed (subset; other tests in solution may be skipped in filter output per harness).
- **`run_verification.py`:** **Overall: PASS** → `.buildlogs/verification/last_run.json`
- **`verify.ps1 -Quick`:** **exit 0** → `artifacts/verify/20260428_015441/verification_report.md`

## Known limitations

- Recent list is **process-local**; restart clears it.
- Ensemble path may record **zero** duration when not supplied by status payload.

## Proof artifacts

| Artifact | Path |
|----------|------|
| Verify report (Quick) | `artifacts/verify/20260428_015441/verification_report.md` |
| Gate/ledger JSON | `.buildlogs/verification/last_run.json` |
