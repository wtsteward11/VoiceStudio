# GOV-VOICESTUDIO-GAP048-ONE-CLICK-STUDIO-SOUND-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP048-ONE-CLICK-STUDIO-SOUND-01 |
| **GAP** | GAP-048 (One-click “Studio Sound”) |
| **Status** | **Closed** |
| **Phase** | Bounded execution row — `EffectsMixerView` / `EffectsMixerViewModel` |
| **Role** | UI Engineer |

## §1 Objective (frozen)

Deliver a bounded one-click **Studio Sound** action on the Effects Mixer panel that creates and applies a curated effect chain (**denoise → compressor → normalize**) to the selected audio using the canonical `IEffectChainClient.ProcessAudioWithChainAsync` path, producing a new derived artifact while preserving the original.

## §2 Hard IN

- One **Studio Sound** button on `EffectsMixerView` (enabled when `SelectedProjectId` + `SelectedAudioId` are set and not busy).
- Curated chain: `denoise` → `compressor` → `normalize` (defaults via existing VM parameter helpers).
- Chain created via `IEffectChainClient.CreateEffectChainAsync` with name `"Studio Sound"`; processing via `IEffectChainClient.ProcessAudioWithChainAsync` (`bypassChain: false`, `preview: false`).
- Transient chain deleted after processing (`DeleteEffectChainAsync`) so the chain store stays clean; no insertion into the visible `EffectChains` list from this path.
- Session-only `StudioSoundOutputAudioId` on the ViewModel (ephemeral).
- Progress: `IsLoading` + `IsStudioSoundRunning` + `ProgressRing` binding.
- Toasts via `ToastNotificationService` on success/failure.
- Tests: `Gap048Tests` (8 source seam scans) + extended `EffectsMixerViewModelSeamTests`.
- AutomationId: `EffectsMixerView_StudioSoundButton`.
- Governance: closure report, tracker, STATE, CANONICAL_REGISTRY, openmemory.

## §3 Hard OUT

- No new backend route or DSP fork.
- No generic mastering suite, preset marketplace, or batch orchestration.
- No Studio Sound preview path (apply-only; GAP-039 covers manual preview elsewhere).
- No timeline integration changes.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Chain create/update/delete | `IEffectChainClient` |
| Chain application / artifact | `IEffectChainClient.ProcessAudioWithChainAsync` |
| DSP | Backend `PostFXProcessor` (existing) |
| UI | `EffectsMixerView` / `EffectsMixerViewModel` |

## §5 Acceptance criteria

- [x] Studio Sound button on `EffectsMixerView` with AutomationId `EffectsMixerView_StudioSoundButton`.
- [x] `CreateEffectChainAsync` + `ProcessAudioWithChainAsync` used; chain order denoise → compressor → normalize.
- [x] `StudioSoundOutputAudioId` set on success; honest error surface on failure.
- [x] `CanRunStudioSound` gates on project/audio/`IsLoading`/`IsStudioSoundRunning`.
- [x] `Gap048Tests` (8) + extended seam tests + full App.Tests + `verify.ps1 -Quick` + `check_ibackendclient_creep.py` PASS.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
python scripts/ci/check_ibackendclient_creep.py
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap048Tests|FullyQualifiedName~EffectsMixerViewModelSeamTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| Transient chain delete fails | Logged; user still gets process result |
| Duplicate “Studio Sound” chain names | Transient id + delete after run |

## §8 Rollback

Revert VM + XAML + tests + docs; no migration.
