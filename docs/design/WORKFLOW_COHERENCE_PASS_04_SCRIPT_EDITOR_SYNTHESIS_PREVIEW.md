# Workflow Coherence Pass 04 — Script Editor → Synthesis / Preview

**Purpose:** Bounded pass to align script/segment content with synthesis requests, profile/engine context, preview (playback) behavior, and honest failure surfaces—without rewriting the Script Editor or transport layer.  
**Date:** 2026-03-24  
**Status:** **Complete** (2026-03-24). **Authoritative proof:** `artifacts/verify/20260324_070722` (PASSED; `artifacts/verify/latest_pointer.json` aligned).

**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) (Workflow 4), [WORKFLOW_COHERENCE_PASS_03_SEARCH_PANEL_FOCUS_NAVIGATION.md](WORKFLOW_COHERENCE_PASS_03_SEARCH_PANEL_FOCUS_NAVIGATION.md), [SCRIPT_EDITOR_PLAYBACK_POLICY.md](SCRIPT_EDITOR_PLAYBACK_POLICY.md), [POST_EXTRACTION_TRANSITION_PLAN.md](POST_EXTRACTION_TRANSITION_PLAN.md). **Involved panels/services (as-is):** `ScriptEditorViewModel`, `IScriptEditorClient`, `IVoiceSynthesisService` — not `VoiceSynthesisViewModel` on this hot path (see §1).

**Authoritative prior proof:** Pass 03 closed with `artifacts/verify/20260324_030133` (see [WORKFLOW_PASS_03_GOVERNANCE_RECONCILIATION.md](WORKFLOW_PASS_03_GOVERNANCE_RECONCILIATION.md)).

**This pass proof:** §8 execution record; report `artifacts/verify/20260324_070722/verification_report.md`.

---

## 1. Participating components (as-is, code-truth)

| Component | Role |
|-----------|------|
| [ScriptEditorView.xaml.cs](../../src/VoiceStudio.App/Views/Panels/ScriptEditorView.xaml.cs) | Constructs `ScriptEditorViewModel` with `IScriptEditorClient`, `IVoiceSynthesisService`, `IAudioPlayerService` (optional); script/segment context menus; `HandleSegmentMenuClick` → `GenerateSegmentCommand` / `PlaySegmentCommand`; comment: script-level synthesize **disabled** (backend 501) — use segment Generate |
| [ScriptEditorView.xaml](../../src/VoiceStudio.App/Views/Panels/ScriptEditorView.xaml) | Script list, segment list, CRUD buttons; segment actions via context menu (not inline Generate/Play buttons in XAML) |
| [ScriptEditorViewModel.cs](../../src/VoiceStudio.App/ViewModels/ScriptEditorViewModel.cs) | `LoadScriptsAsync` → `IScriptEditorClient.GetScriptsAsync(SelectedProjectId, …)`; `GenerateSegmentAsync` → [`ScriptEditorSynthesisRequestBuilder`](../../src/VoiceStudio.App/Utilities/ScriptEditorSynthesisRequestBuilder.cs) → `IVoiceSynthesisService.SynthesizeVoiceAsync` → `UpdateScriptAsync` with `GeneratedAudioId`; `PlaySegmentAsync` → `IAudioPlayerService.PlayBackendAudioIdAsync` + [`BackendPlaybackBaseUrl.Resolve`](../../src/VoiceStudio.App/Utilities/BackendPlaybackBaseUrl.cs); `NavigateToScriptAsync` for search |
| [ScriptEditorSynthesisRequestBuilder.cs](../../src/VoiceStudio.App/Utilities/ScriptEditorSynthesisRequestBuilder.cs) | `Build(segment, scriptMetadata, trimmedText, profileId)` — engine/language policy (Pass 04 C1/C2) |
| [BackendPlaybackBaseUrl.cs](../../src/VoiceStudio.App/Utilities/BackendPlaybackBaseUrl.cs) | Canonical playback base URL from `BackendClientConfig` (Pass 04 C4) |
| [IScriptEditorClient.cs](../../src/VoiceStudio.App/Core/Services/IScriptEditorClient.cs) | Script CRUD, segments; **no** synthesis API on interface |
| [ScriptEditorClient.cs](../../src/VoiceStudio.App/Services/ScriptEditorClient.cs) | HTTP to `/api/script-editor` via `BackendClientHttpPipeline` |
| [IVoiceSynthesisService.cs](../../src/VoiceStudio.App/Services/IVoiceSynthesisService.cs) | `SynthesizeVoiceAsync`, `GetAudioStreamAsync` |
| [VoiceSynthesisService.cs](../../src/VoiceStudio.App/Services/VoiceSynthesisService.cs) | Delegates to `IBackendClient`; shapes request (default engine `xtts`, non-empty text); normalizes `AudioUrl` |
| `ScriptSegment` / `Script` | [Script.cs](../../src/VoiceStudio.App/Core/Models/Script.cs) — segment fields include `VoiceProfileId`, `GeneratedAudioId`, `GenerationEngineId`, generation metadata |
| [SCRIPT_EDITOR_PLAYBACK_POLICY.md](SCRIPT_EDITOR_PLAYBACK_POLICY.md) | **Local-only** segment playback; no global transport |

**Not on the hot path (correct backlog drift):** `VoiceSynthesisViewModel` is **not** used by Script Editor today. Workflow 4 backlog row naming it alongside Script Editor is **imprecise**; Pass 04 should treat **IVoiceSynthesisService** as the synthesis seam for this panel unless scope explicitly expands.

---

## 2. As-is workflow map (code-truth)

### 2.1 Script load and project context

1. **Activation:** `IPanelLifecycle.OnActivatedAsync` → `LoadScriptsAsync` ([ScriptEditorViewModel.cs](../../src/VoiceStudio.App/ViewModels/ScriptEditorViewModel.cs)).
2. **Project filter:** `GetScriptsAsync(SelectedProjectId, SearchQuery, …)` — if `SelectedProjectId` is null/empty, API returns scripts per client contract (user must pick project in UI for create; load behavior depends on selection).
3. **Search navigation:** `NavigateToScriptAsync` loads script via `GetScriptAsync`, sets `SelectedProjectId` from script, reloads list, selects script — used by `INavigatablePanel` from global search (Pass 03).

### 2.2 Segment creation

1. `AddSegmentAsync` creates `ScriptSegment` with placeholder text and **`VoiceProfileId = null`** ([ScriptEditorViewModel.cs](../../src/VoiceStudio.App/ViewModels/ScriptEditorViewModel.cs) ~L499–504).
2. Persists via `AddSegmentToScriptAsync`.

### 2.3 Segment generation (synthesis)

1. **Entry:** Segment list **right-tap** → **Generate** → `HandleSegmentMenuClick` → `GenerateSegmentCommand.ExecuteAsync` ([ScriptEditorView.xaml.cs](../../src/VoiceStudio.App/Views/Panels/ScriptEditorView.xaml.cs)).
2. **CanExecute:** `EnhancedAsyncRelayCommand` predicate: segment non-null, `!IsLoading`, `_voiceSynthesisService != null`, non-empty `VoiceProfileId`, non-whitespace `Text` ([ScriptEditorViewModel.cs](../../src/VoiceStudio.App/ViewModels/ScriptEditorViewModel.cs) L158–164). Silent no-op if user invokes while false (context menu still offers Generate).
3. **Request:** [`ScriptEditorSynthesisRequestBuilder.Build`](../../src/VoiceStudio.App/Utilities/ScriptEditorSynthesisRequestBuilder.cs)(segment, `SelectedScript?.Metadata`, trimmed text, profile): **Engine** = trimmed `segment.GenerationEngineId` if set, else `ScriptEditorSynthesisDefaults.DefaultEngine` (`xtts`); **Language** = script metadata key `synthesis_language` or `language` if present, else `DefaultLanguage` (`en`).
4. **Call:** `IVoiceSynthesisService.SynthesizeVoiceAsync` → `IBackendClient` (see [VoiceSynthesisService.cs](../../src/VoiceStudio.App/Services/VoiceSynthesisService.cs)).
5. **Persist:** On success with `AudioId`, builds updated segment list with `GeneratedAudioId`, `GenerationEngineId`, etc., then `UpdateScriptAsync`.
6. **Reload:** `LoadScriptsAsync` + restore `SelectedScript` / `SelectedSegment` by id.
7. **Failure:** `ErrorMessage` + toast; exceptions mapped in `VoiceSynthesisService` to user-facing messages.

### 2.4 Preview / playback

1. **Entry:** Context menu **Play** only if `GeneratedAudioId` non-empty ([ScriptEditorView.xaml.cs](../../src/VoiceStudio.App/Views/Panels/ScriptEditorView.xaml.cs) L231–237).
2. **Call:** `IAudioPlayerService.PlayBackendAudioIdAsync(GeneratedAudioId, baseUrl, onComplete)`; `baseUrl` = `BackendPlaybackBaseUrl.Resolve(AppServices.GetService<BackendClientConfig>())` ([ScriptEditorViewModel.cs](../../src/VoiceStudio.App/ViewModels/ScriptEditorViewModel.cs) L741–747).
3. **Policy:** Local-only; see [SCRIPT_EDITOR_PLAYBACK_POLICY.md](SCRIPT_EDITOR_PLAYBACK_POLICY.md).

### 2.5 Script-level “synthesize / preview”

1. **Intentionally absent in UI:** Script context menu has **no** Synthesize action; comment states backend 501 and directs users to segment Generate ([ScriptEditorView.xaml.cs](../../src/VoiceStudio.App/Views/Panels/ScriptEditorView.xaml.cs) L190).
2. **Help copy** still mentions “export / batch synthesis” aspirationally — not wired in this pass’s scope map as implemented behavior.

### 2.6 Stop-short / degradation points

| Step | Behavior |
|------|----------|
| Generate with null profile | Command disabled; user sees no Generate unless profile set — may confuse if UI does not surface why |
| Generate with empty text | `ErrorMessage` set, early return |
| Synthesis returns no `AudioId` | `ErrorMessage` + warning toast (`ScriptEditor.SynthesisReturnedNoAudioId`); **no** `UpdateScriptAsync` |
| `UpdateScriptAsync` null after synthesis | `PersistFailed` error |
| Play without audio | Play menu hidden; command returns early |
| `IVoiceSynthesisService` null in tests / mis-DI | Constructor allows null optional — production [ScriptEditorView.xaml.cs](../../src/VoiceStudio.App/Views/Panels/ScriptEditorView.xaml.cs) resolves real service |

---

## 3. Target behavior (Pass 04 — achieved)

1. **Context coherence:** Engine repeats last successful generation via `GenerationEngineId`; language from script metadata keys or documented defaults (`ScriptEditorSynthesisDefaults`).
2. **Request coherence:** Single path: `ScriptEditorSynthesisRequestBuilder.Build` (tests assert contract).
3. **Preview coherence:** Playback uses `BackendPlaybackBaseUrl.Resolve` (same helper intent as other panels).
4. **Honest failures:** Missing `AudioId`, missing script context, persist failure, and play failure each surface distinct messages/toasts where implemented.
5. **Documentation truth:** Backlog Workflow 4 row aligned to `IVoiceSynthesisService` / `IAudioPlayerService` / `IScriptEditorClient` (C6).

---

## 4. Current defects / coherence gaps (pre-pass inventory; see §10 for resolution)

| ID | Symptom | Files / classes | Likely cause | Priority | Pass 04 |
|----|---------|-----------------|--------------|----------|---------|
| D1 | Engine/language fixed to `xtts` / `en` for all script segments | `GenerateSegmentAsync` | No segment-level engine/language UI | High | **Addressed:** builder + sticky `GenerationEngineId` + metadata language keys |
| D2 | New segments start with `VoiceProfileId = null` | `AddSegmentAsync`, `GenerateSegmentCommand` gate | Product/onboarding gap | Med | **Open** (out of scope) |
| D3 | Synthesis success without `AudioId` yields no explicit user message | `GenerateSegmentAsync` | Missing else-branch UX | Med | **Addressed:** `ErrorMessage` + warning toast; no persist |
| D4 | `VoiceSynthesisViewModel` in backlog but not used | Backlog | Stale inventory | Low | **Addressed:** backlog row (C6) |
| D5 | Help text suggests broader capabilities | `ScriptEditorView` help | Copy ahead of implementation | Low | **Open** (optional follow-up) |
| D6 | Ad hoc localhost in play path | `PlaySegmentAsync` | Policy drift | Med | **Addressed:** `BackendPlaybackBaseUrl.Resolve` |
| D7 | Menu offers Generate when `CanExecute` false | `HandleSegmentMenuClick` | UX gap | Low | **Deferred** (C5 OUT — §10) |

*(Hypothesis labeled in matrix where cause not fully traced across all branches.)*

---

## 5. Bounded change matrix (frozen intent — executed; see §10)

| Change ID | Target behavior | Primary owner | Supporting | Scenarios | Tests | Proof |
|-----------|-----------------|---------------|------------|-----------|-------|-------|
| C1 | Engine/language/profile propagation policy defined and implemented (per-segment or inherited) | `ScriptEditorViewModel` | `ScriptSegment` model, UI bindings if needed | Generate | Extend `ScriptEditorViewModel` tests / seam tests | dotnet test |
| C2 | Normalize `VoiceSynthesisRequest` construction (single helper; defaults explicit) | `ScriptEditorViewModel` or small helper | `VoiceSynthesisService.ShapeRequest` interaction | Generate | Unit tests on request shape | dotnet test |
| C3 | Explicit UX when synthesis returns without audio id | `GenerateSegmentAsync` | — | Generate edge case | Unit test | dotnet test |
| C4 | Playback base URL from one canonical config path; align with transport policy | `PlaySegmentAsync` | `BackendClientConfig` | Play | Mock audio player tests | dotnet test |
| C5 | Optional: disable or annotate context menu when Generate cannot run | `ScriptEditorView.xaml.cs` | — | Segment menu | UI test optional / manual | manual |
| C6 | Align Workflow 4 backlog + help copy with actual seams (`IVoiceSynthesisService`) | Docs | — | — | Doc review | — |

Exact rows may be trimmed at implementation time; **do not** add rows that violate §6.

---

## 6. Strict out-of-scope

- No broad Script Editor rewrite or large XAML redesign.
- No new `IBackendClient` extraction / transport moves (extraction paused per transition plan).
- No global playback / transport architecture change (contradicts [SCRIPT_EDITOR_PLAYBACK_POLICY.md](SCRIPT_EDITOR_PLAYBACK_POLICY.md) unless ADR amends policy).
- No recording/import/transcription workflow (Pass 05 territory).
- No backup/restore recovery flow (Pass 06 territory).
- No shell / panel host / search overlay changes except where strictly required for script synthesis UX (default: **none**).
- No cosmetic-only UI polish or unrelated refactors.

---

## 7. Proof expectations (define before implementation)

Minimum proof standard (same discipline as Pass 03):

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. **Targeted tests** (Pass 04 closure filter):

   ```text
   dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ScriptEditorViewModel|FullyQualifiedName~VoiceSynthesisService|FullyQualifiedName~ScriptEditorVisibility|FullyQualifiedName~ScriptEditorSynthesisRequestBuilder"
   ```

   **Closure run (2026-03-24):** 39 passed, 0 failed (includes `ScriptEditorSynthesisRequestBuilderTests`).

3. `.\scripts\verify.ps1 -Quick`
4. Record **authoritative** artifact directory from `artifacts/verify/latest_pointer.json` (must contain `verification_report.md` + `summary.json`).
5. Update §8 execution record with **exact** files changed and commands run.
6. Update `.cursor/STATE.md` proof index and `CROSS_FEATURE_WORKFLOW_BACKLOG.md` Workflow 4 row when Pass 04 closes.

---

## 8. Execution record (closure)

| Item | Detail |
|------|--------|
| **Status** | **Complete** (2026-03-24) |
| **Behavior** | Centralized segment synthesis request (`ScriptEditorSynthesisRequestBuilder`); metadata language + sticky engine; honest no-`AudioId` path; playback URL via `BackendPlaybackBaseUrl`; strings `ScriptEditor.SynthesisReturnedNoAudioId`, `Toast.Title.SegmentGenerateIncomplete`. |
| **Files changed (primary)** | `ScriptEditorViewModel.cs`, `ScriptEditorSynthesisRequestBuilder.cs`, `BackendPlaybackBaseUrl.cs`, `Resources*.resw`, `ScriptEditorViewModelTests.cs`, `ScriptEditorSynthesisRequestBuilderTests.cs` |
| **Build** | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — PASS |
| **Tests** | Filter in §7 — **39 passed**, 0 failed (2026-03-24) |
| **verify.ps1 -Quick** | **Authoritative:** `artifacts/verify/20260324_070722` (PASSED). `artifacts/verify/latest_pointer.json` aligned. Report: `artifacts/verify/20260324_070722/verification_report.md`. |
| **Known leftovers** | D2 profile onboarding; D5 help copy; D7 / C5 menu affordance deferred; no E2E WinAppDriver for script generate/play; per-segment engine/language UI not added (metadata + sticky engine only). |

---

## 9. Audit-grade gaps (post-closure honesty)

| Area | State after Pass 04 | Residual |
|------|---------------------|----------|
| Engine/language | Builder + metadata + sticky `GenerationEngineId` | No dedicated segment UI for engine/language beyond metadata |
| Profile on new segment | Unchanged | D2 — user must assign profile |
| Synthesis OK, no `AudioId` | Warning + `ErrorMessage`; no persist; tests | — |
| Playback URL | `BackendPlaybackBaseUrl.Resolve` | Custom `BackendClientConfig` in tests uses default URL; no dedicated test for custom base URL registration |
| E2E script → generate → play | Unit/seam only | Manual / future automation |

---

## 10. Implementation lock (matrix-to-code — executed)

### 10.1 Locked scope

- **In scope (implemented):** **C1**, **C2**, **C3**, **C4**, **C6** (docs/backlog).
- **C5:** **OUT / DEFERRED** — disable/annotate segment context menu when Generate cannot run would be UI-only creep; D7 accepted for this pass. Revisit under a UI polish or Pass 05 prep if needed.

### 10.2 Matrix-to-code mapping

| Change | Methods / types | Behavior | Tests proving |
|--------|-----------------|----------|---------------|
| **C1** | `ScriptEditorSynthesisRequestBuilder.Build`, `ScriptEditorSynthesisDefaults` | Engine: non-empty trimmed `segment.GenerationEngineId` else `DefaultEngine` (`xtts`). Language: `synthesis_language` then `language` in `script.Metadata` else `DefaultLanguage` (`en`). | `ScriptEditorSynthesisRequestBuilderTests`; `GenerateSegmentAsync_UsesSegmentGenerationEngineId_WhenSet`; `GenerateSegmentAsync_UsesScriptMetadataLanguage_WhenSet` |
| **C2** | Same builder; `GenerateSegmentAsync` calls `Build(segment, SelectedScript?.Metadata, text, profileId)` | Single normalization path for segment synthesis requests. | `GenerateSegmentAsync_PersistsGeneratedAudioId_AndEnablesPlay` (captures request) |
| **C3** | `GenerateSegmentAsync` — branch `response == null \|\| string.IsNullOrEmpty(response.AudioId)` | Sets `ErrorMessage`, `ShowWarning`, **never** `UpdateScriptAsync`. Separate branch when audio returned but `SelectedScript == null`. | `GenerateSegmentAsync_WhenSynthesisReturnsEmptyAudioId_DoesNotPersist`; `GenerateSegmentAsync_WhenSynthesisReturnsAudio_ButNoSelectedScript_DoesNotPersist` |
| **C4** | `PlaySegmentAsync` | `baseUrl = BackendPlaybackBaseUrl.Resolve(AppServices.GetService<BackendClientConfig>())`. Play errors → `ErrorMessage` + error toast (existing catch). | `PlaySegmentCommand_ExecuteAsync_CallsPlayBackendAudioIdAsync` |
| **C5** | — | Deferred (§10.1). | — |
| **C6** | `CROSS_FEATURE_WORKFLOW_BACKLOG.md` Workflow 4 | Current wiring documents generation / playback / persist seams. | Doc review |

### 10.3 Expansion guard

Any work on script-level synthesis, batch synthesis, transport extraction, or global playback architecture is **out of scope** per §6.

---

## Changelog

| Date | Note |
|------|------|
| 2026-03-24 | Scope frozen: as-is map, defects, matrix, out-of-scope, proof expectations. |
| 2026-03-24 | Line-number and model-path corrections; §9 audit residuals; `GenerateSegmentCommand` CanExecute wording aligned to code. |
| 2026-03-24 | **Closure:** C1–C4 + C6 implemented; C5 deferred; §10 lock; proof `artifacts/verify/20260324_070722`; §7 filter includes `ScriptEditorSynthesisRequestBuilder`. |
