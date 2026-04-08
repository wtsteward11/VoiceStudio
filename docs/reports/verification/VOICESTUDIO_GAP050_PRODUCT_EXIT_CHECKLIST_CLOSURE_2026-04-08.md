# VOICESTUDIO — GAP-050 Product Exit Checklist — Lane Closure (2026-04-08)

**Execution row:** [GOV_VOICESTUDIO_GAP050_PRODUCT_EXIT_CHECKLIST_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP050_PRODUCT_EXIT_CHECKLIST_01_EXECUTION_ROW.md) **Closed**  
**Lane type:** **proof-hardening** — no `src/`, `backend/`, `app/`, or test code changes in this lane.

## 1. Goal

Audit whether **GAP-050** (*Emotional voice control + preview*, roadmap F5-6) can close as a **product umbrella** after three bounded runtime lanes, or must spawn a **single** named residual lane.

## 2. Original promise vs bounded lanes

| Product facet | Source | Satisfied by (evidence) |
|---------------|--------|-------------------------|
| Canonical **preset → prosody** (no route-local DSP fork for that mapping) | Tracker + F5-6 | **Closed** — [GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_PROSODY_MAPPING_01](../../design/GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_PROSODY_MAPPING_01_EXECUTION_ROW.md); `emotion_preset_prosody_mapper.resolve_emotion_prosody` + `apply_transform` in `POST /api/emotion/apply-extended` ([`backend/api/routes/emotion.py`](../../backend/api/routes/emotion.py) ~L421–L505). |
| **Voice Synthesis** consumer chains base synthesis → emotion apply | Consumer lane | **Closed** — [GOV_VOICESTUDIO_GAP050_VOICESYNTHESIS_EMOTION_PRESET_CONSUMER_01](../../design/GOV_VOICESTUDIO_GAP050_VOICESYNTHESIS_EMOTION_PRESET_CONSUMER_01_EXECUTION_ROW.md); `VoiceSynthesisService` + `IEmotionControlClient.ApplyEmotionAsync`. |
| **State** persistence / profile switch / restore hygiene | State lane | **Closed** — [GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_01](../../design/GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_01_EXECUTION_ROW.md); `IPanelStatePersistable`, canonical four presets, single VM `DataContext`. |
| **Failure / degradation honesty** on synthesis path | Consumer lane | **Closed** — `EmotionPresetApplyFailureMessage`, combined toasts, `prosody_handling` on apply-extended response. |
| **Diagnostics metadata** on apply | Mapping lane | **Closed** — `prosody_handling`, `emotion_mapping_source` on `EmotionApplyExtendedResponse`. |
| **Preview** (“with preview”) for Emotion Control | F5-6 title | **Not satisfied** — see §3. |

**Dependency:** **GAP-023** prosody authority — **Closed** (prerequisite for mapping lane).

## 3. Residual seam (product-level)

### 3.1 Backend: `/api/emotion/preview` is non-authoritative

[`backend/api/routes/emotion.py`](../../backend/api/routes/emotion.py) `preview_emotion` (~L747–L757) returns a static JSON object with `audio_url: null`, `duration: 0.0`, message `"Preview generated"`. It does **not**:

- load `audio_id` from the request into prosody processing,
- call `resolve_emotion_prosody`,
- call `apply_transform`.

### 3.2 Contract drift: client vs server preview model

- **Client:** [`EmotionControlClient.PreviewEmotionAsync`](../../src/VoiceStudio.App/Services/EmotionControlClient.cs) POSTs **`EmotionApplyExtendedRequest`** JSON (`audio_id`, `primary_emotion`, `primary_intensity`, …) to `/api/emotion/preview`.
- **Server:** `EmotionPreviewRequest` expects `text`, `audio_id`, `emotion`, `intensity`, `blend` — **not** `primary_emotion` / blending fields. Unmapped client fields are ignored; preview does not reflect selected primary/secondary emotions.

### 3.3 UI expectation

[`EmotionControlViewModel.PreviewEmotionAsync`](../../src/VoiceStudio.App/ViewModels/EmotionControlViewModel.cs) sets `PreviewAudioId` / `PreviewAudioUrl` from `EmotionPreviewResponse`. The stub response carries **no** `audio_id`, so playback cannot succeed.

### 3.4 Other consumers (audit notes)

| VM | Path | GAP-050 umbrella note |
|----|------|----------------------|
| `VoiceSynthesisViewModel` | Canonical preset + `VoiceSynthesisService` | **In scope** — closed lanes. |
| `EmotionControlViewModel` | **Apply** → apply-extended; **Preview** → broken stub | **Preview** = §3 residual. |
| `EmotionStylePresetEditorViewModel` | `IVoiceSynthesisService.SynthesizeVoiceAsync` for preview | Uses synthesis stack; **not** the `/api/emotion/preview` stub. |
| `EmotionStyleControlViewModel` | `/api/emotion-style` | **Separate** preset store / API; not the bounded canonical preset→prosody table; **out of this exit audit’s residual seam** (no second named lane). |

## 4. Verdict

- **Close GAP-050 product umbrella:** **No** — preview seam is **product-critical** relative to F5-6 wording and Emotion Control UX.
- **Spawn exactly one bounded lane:** **Yes** — [GOV_VOICESTUDIO_GAP050_EMOTION_PREVIEW_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP050_EMOTION_PREVIEW_AUTHORITY_01_EXECUTION_ROW.md) (**Frozen**, awaiting implementation).

## 5. Known limitations (unchanged, not blocking audit closure)

- **`timeline_curve`** on apply-extended: warning only; not applied per mapping lane Hard OUT.
- **Streaming** emotion path: explicitly deferred on prior lanes.
- **Pytest import shadow** for isolated `test_emotion.py`: documented; authoritative run remains `pytest tests/ci`.

## 6. Verification (this lane)

| Step | Command / artifact | Result |
|------|-------------------|--------|
| Rolling verifier | `python scripts/run_verification.py` | PASS (**completion_guard**); `last_run.json` **20260407-194132** (post-commit `b49addbb`; STATE back-fill `725b3fa7`) |
| Doc consistency | Tracker GAP-050 **Open** + exit row + spawned row; registry; STATE | PASS |

**Proof inheritance:** Runtime proof remains on last GAP-050 runtime lane — [VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_LANE_CLOSURE_2026-04-07.md](VOICESTUDIO_GAP050_EMOTION_PRESET_STATE_HYGIENE_AND_PERSISTENCE_LANE_CLOSURE_2026-04-07.md); App.Tests **3193** passed / **274** skipped; `pytest tests/ci` **217**; rolling **20260407-190416** (**completion_guard** PASS). This closure adds **governance + audit traceability** only.

## 7. Rollback

Revert this lane’s commits; remove registry addendum; restore prior GAP-050 tracker row text; clear spawned preview row if audit is invalidated.
