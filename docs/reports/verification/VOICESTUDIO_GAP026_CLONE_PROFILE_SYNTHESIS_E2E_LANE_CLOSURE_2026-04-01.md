# VoiceStudio GAP-026 Clone → Profile → Synthesis E2E Lane Closure — 2026-04-01

**Lane:** GOV-VOICESTUDIO-GAP026-CLONE-PROFILE-SYNTHESIS-E2E-01 — activation-time sync with `IContextManager.ActiveProfileId` on **Voice Synthesis**; **ProfileSelectedEvent** after clone finalize so active consumers update immediately; **no** shell `NavigateToEvent` wiring (known limitation).  
**Execution row:** [GOV_VOICESTUDIO_GAP026_CLONE_PROFILE_SYNTHESIS_E2E_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP026_CLONE_PROFILE_SYNTHESIS_E2E_01_EXECUTION_ROW.md) **Closed**.  
**Tracker:** **GAP-026** **Closed** — [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).  
**Product:** **GAP-045** remains **Open**.

## 0) Verification provenance

**Label:** **Independently repo-verified locally** — matrix below executed in the same session as GAP-028 closure (shared proof caps).

## 1) Scope summary

- **`VoiceSynthesisViewModel.OnActivatedAsync`:** After subscribing to `ProfileSelectedEvent`, syncs from `AppServices.TryGetContextManager()` when `ActiveProfileId` differs from `SelectedProfile?.Id` (synthetic `ProfileSelectedEvent` from source `context-manager-sync`).
- **`VoiceCloningWizardViewModel.FinalizeWizardAsync`:** After successful clone, publishes `ProfileSelectedEvent` with `InteractionIntent.ImmediateUse` following `ProfileCreatedEvent`.
- **`NavigateToEvent("voice-synthesis")`:** Still not handled by shell navigation — documented comment only; profile propagation is via context + event bus.

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings in other files) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3009** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed, **2** deselected |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260401_232510/verification_report.md` |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` (`timestamp_short` **20260401-233116**) |

## 3) Proof artifacts (code + tests)

- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — `OnActivatedAsync` context sync.
- `src/VoiceStudio.App/ViewModels/VoiceCloningWizardViewModel.cs` — `ProfileSelectedEvent` after finalize; navigation comment.
- `src/VoiceStudio.App.Tests/ViewModels/Gap026CloneProfileSynthesisTests.cs` — activation + wizard event order / failure.

## 4) Honest limits

- Automatic **panel switch** to Voice Synthesis after clone is **out of scope** (shell / GAP-007 territory).
- **GAP-045** broader scope remains Open per tracker.

## 5) Closure

**GOV-VOICESTUDIO-GAP026-CLONE-PROFILE-SYNTHESIS-E2E-01:** **Closed** 2026-04-01 with proof-backed acceptance per execution row.

**Next hero-path:** [GAP-028](../../design/GOV_VOICESTUDIO_GAP028_TRAINING_PROFILE_METADATA_REFRESH_01_EXECUTION_ROW.md) closed in the same session — see [VOICESTUDIO_GAP028_TRAINING_PROFILE_METADATA_REFRESH_LANE_CLOSURE_2026-04-01.md](VOICESTUDIO_GAP028_TRAINING_PROFILE_METADATA_REFRESH_LANE_CLOSURE_2026-04-01.md).
