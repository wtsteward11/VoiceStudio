# GOV-VOICESTUDIO-GAP050-EMOTION-PREVIEW-AUTHORITY-01 — Emotion Control preview uses canonical prosody authority

**Lane ID:** `GOV_VOICESTUDIO_GAP050_EMOTION_PREVIEW_AUTHORITY_01`  
**Status:** **Frozen** (2026-04-08) — **not implemented**; awaits Construct phase.  
**Tracker:** [GAP-050](PROFESSIONAL_GAP_TRACKER.md) — product umbrella **Open** until this lane **Closed**.  
**Lane type:** **runtime-affecting**  
**Depends on:** [GOV_VOICESTUDIO_GAP050_PRODUCT_EXIT_CHECKLIST_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP050_PRODUCT_EXIT_CHECKLIST_01_EXECUTION_ROW.md) (audit **Closed**); [GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_PROSODY_MAPPING_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_PROSODY_MAPPING_01_EXECUTION_ROW.md); [GOV_VOICESTUDIO_GAP023_PROSODY_AUTHORITY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP023_PROSODY_AUTHORITY_01_EXECUTION_ROW.md)

## Problem statement

`POST /api/emotion/preview` returns a **placeholder** JSON payload (`audio_url: null`, no `audio_id`) and does **not** invoke `resolve_emotion_prosody` or `apply_transform`. The WinUI client (`EmotionControlClient.PreviewEmotionAsync`) posts an `EmotionApplyExtendedRequest`-shaped body to `/api/emotion/preview`, while the FastAPI handler models **`EmotionPreviewRequest`** (`emotion`, `intensity`, …) — **`primary_emotion` / intensities are not bound**, so preview does not reflect the user’s selected emotions. `EmotionControlViewModel.PreviewEmotionAsync` expects a playable `audio_id` and will not receive one from the current backend.

This violates the product wording **“with preview”** for the Emotion Control panel and breaks parity with **apply-extended**, which is already canonical.

## State ownership (frozen)

| Surface | Owner |
|---------|--------|
| Preview HTTP contract | `backend/api/routes/emotion.py` + Pydantic models |
| Client preview call | `EmotionControlClient` / `IEmotionControlClient` |
| UI command | `EmotionControlViewModel.PreviewEmotionCommand` |

## Acceptance contract (Close) — to be checked at implementation

- [ ] `POST /api/emotion/preview` accepts the same authoritative inputs as preview needs (either align model to `EmotionApplyExtendedRequest` shape **or** dedicated preview request that maps 1:1 to `resolve_emotion_prosody` inputs).
- [ ] Preview path calls **`resolve_emotion_prosody`** then **`apply_transform`** (or a documented shared helper used by `apply-extended`) — **no second DSP fork**.
- [ ] Response returns a **non-empty** `audio_id` (and `audio_url` consistent with apply-extended) when inputs and audio artifact are valid; honest **4xx/503** when not.
- [ ] `prosody_handling` / `emotion_mapping_source` parity with apply-extended **or** documented intentional subset with honest warnings.
- [ ] Tests: route-level pytest for preview success + failure; optional C# contract test if response shape changes.
- [ ] Closure matrix per runtime lane standard; tracker GAP-050 row updated (umbrella **Closed** if no further seams); registry + STATE.

## Allowlist (implementation — indicative)

`backend/api/routes/emotion.py`, `backend/api/models*` if needed, `tests/unit/backend/api/routes/test_emotion.py`, `src/VoiceStudio.App/Services/EmotionControlClient.cs`, `src/VoiceStudio.Core/Models/EmotionControlModels.cs` (or equivalent), `src/VoiceStudio.App.Tests/` as needed, execution row, closure report, tracker, registry, `.cursor/STATE.md`.

## Hard OUT

- Reintroducing route-local librosa pitch/time forks (use prosody authority only).
- Expanding `IBackendClient` on `EmotionControlViewModel`.
- Streaming synthesis rewrite, shell/startup changes, new emotion ML models.
- Bundling this work with unrelated GAP-050 scope (e.g. `timeline_curve` application — separate row if prioritized).

## Rollback

Revert this lane’s commits; preserve closed mapping, consumer, state-hygiene lanes and GAP-023.

## Changelog

- **2026-04-08:** Row **Frozen** — spawned from [GOV_VOICESTUDIO_GAP050_PRODUCT_EXIT_CHECKLIST_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP050_PRODUCT_EXIT_CHECKLIST_01_EXECUTION_ROW.md) audit.
