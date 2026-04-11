# VoiceStudio — OpenMemory

This file is a **living index** of VoiceStudio’s architecture, contracts, and current recovery gate status.

## Overview

- **Primary goal**: upgrade voice cloning quality + functionality without architectural drift.
- **Architecture**:
  - **Frontend**: WinUI 3 (.NET) app under `src/` (MVVM).
- **UI styling**: `src/VoiceStudio.App/Resources/DesignTokens.xaml` holds VSQ tokens; style dictionaries live under `src/VoiceStudio.App/Resources/Styles/` and are merged in `App.xaml` (Controls/Text/Panels).
- **Frontend DI**: `AppServiceBootstrapper` builds DI container; `AppServices` provides access; legacy `ServiceProvider` is a shim.
- **App state**: `AppStateStore` (immutable) tracks backend connectivity and global app flags.
  - **Backend API**: Python FastAPI service under `backend/`.
  - **GAP-057 (2026-04-10)**: `/api/audio/*` and `/api/audio/audit/*` use router-level `Depends(require_auth_if_enabled)` (same gate as `/api/voice/*`). GET JSON response cache keys include `AUTH_REQUIRED` + credential presence (`backend/api/response_cache.py` `_cache_key_auth_segment`) so cached 200s cannot bypass auth.
  - **GAP-058 (2026-04-10)**: App-level WebSockets `/ws/realtime` and `/ws/plugins` use `require_ws_auth_if_enabled` (`WS_CLOSE_AUTH_REQUIRED=4001`); `/ws/events` stays public (heartbeat only). C# `WebSocketService` / `PluginBridgeService` send `X-API-Key` / `Authorization` on handshake via `SetAuthHeaders` / `SetCredentialProvider` + optional `IUnifiedAuthService` from `BackendClient`; `src/VoiceStudio.App/Properties/InternalsVisibility.cs` emits `InternalsVisibleTo` for seam tests (`GenerateAssemblyInfo=false`). Closure: `docs/reports/verification/VOICESTUDIO_GAP058_WEBSOCKET_AUTH_LANE_CLOSURE_2026-04-10.md`.
  - **GAP-059 (2026-04-11)**: Canonical trust-audit lane — `TrustAuditService` in `backend/services/trust_audit_service.py` writes structured `TrustAuditEvent` into existing `AuditLogger` JSONL (`metadata`); wired on STS convert (success/denial/failure), transformed-only `POST /api/audio/export` and `GET /api/audio/file/{id}`, and `GET /api/audio/{id}/marking`. Closure: `docs/reports/verification/VOICESTUDIO_GAP059_AUDIT_TRAIL_LANE_CLOSURE_2026-04-11.md`.
  - **GAP-060 (2026-04-11)**: Model provenance on STS outputs — `ModelProvenanceService` in `backend/services/model_provenance_service.py` writes `metadata_json.model_provenance` (engine id/version/name/family from manifest when available; `artifact_id` + `correlation_id` join with GAP-059); `AudioRegistryDB.update_metadata`; best-effort attach after successful `create_audio_artifact_from_file` in `SpeechToSpeechService.convert`. Closure: `docs/reports/verification/VOICESTUDIO_GAP060_MODEL_PROVENANCE_LANE_CLOSURE_2026-04-11.md`.
  - **GAP-061 (2026-04-11)**: RBAC on identity-sensitive trust surfaces — `require_user_role_for_trust_surfaces` (`UserRole.USER` minimum when `VOICESTUDIO_REQUIRE_AUTH=true`; local mode: synthetic `local` principal when anonymous); `POST /api/voice/sts/convert` + `POST /api/audio/export`; `TrustAuditEvent.user_role` on STS/export records; Starlette `TestClient` host `testclient` in `RateLimitMiddleware._LOOPBACK_HOSTS` (removes rate-limit monkey-patch in `test_audio_trust_audit.py`). Closure: `docs/reports/verification/VOICESTUDIO_GAP061_RBAC_WIRING_TRUST_SURFACES_LANE_CLOSURE_2026-04-11.md`.
  - **GAP-069 bounded slice — Backend Readiness Truth (2026-04-11)**: `on_startup_prepare` `[STARTUP-TIMING]` logs; `GET /health` + `/api/health` expose `engines_ready` (`backend/api/startup_flags.py`); `BackendProcessManager` writes `%LocalAppData%\VoiceStudio\crashes\startup_decision.json` schema **2** (success + failure, timing/stderr/python path); Piper engine uses OHF `piper.PiperVoice` when `.onnx` present (`executable_path` `piper_voice_v1`); prerequisites `import backend.api.main` smoke; Grade R `docs/reports/verification/PROOF_GOLDEN_PATH_REAL_2026-04-10.json`. Closure: `docs/reports/verification/VOICESTUDIO_BACKEND_READINESS_TRUTH_LANE_CLOSURE_2026-04-11.md`. Umbrella GAP-069 remains open for continuous CI items.
  - **GAP-069 bounded slice 2 — Backend Readiness Regression Guard (2026-04-11)**: `scripts/ci/check_startup_artifact.py` validates schema v2 startup artifact (hard fail vs advisory timing); `tests/unit/test_startup_artifact_checker.py` **10**; `scripts/ci/run_backend_smoke.py` optional uvicorn smoke → `PROOF_BACKEND_SMOKE_*.json`; `scripts/run_verification.py` includes `startup_artifact_check` (argv list when `VOICESTUDIO_STARTUP_ARTIFACT_PATH` set — fixes Windows path quoting); committed sample `docs/reports/verification/samples/startup_decision_success_v2.json`; skip env `VOICESTUDIO_SKIP_STARTUP_ARTIFACT_CHECK`. Closure: `docs/reports/verification/VOICESTUDIO_BACKEND_READINESS_REGRESSION_GUARD_LANE_CLOSURE_2026-04-11.md`.
  - **Engine layer**: Python engine implementations and runtime management under `app/`.
  - **Shared contracts**: JSON/schema artifacts under `shared/` (interop boundary).
- **Canonical architecture docs**: `docs/architecture/README.md` (Parts 1–9 + Part 10 legacy isolation); legacy archive in `docs/archive/architecture_legacy/`.
- **Layout guidance**: `docs/design/panel_layout_optimization.pdf` (panel layout optimization guidance).
- **Repo/runtime layout docs**: `docs/developer/REPO_LAYOUT.md` and `docs/developer/RUNTIME_LAYOUT.md`.
- **Packaging lane**: unpackaged apphost EXE + installer only (`WindowsPackageType=None`; MSIX archived/removed).
- **WinAppSDK versioning**: `Directory.Build.props` centralizes `MicrosoftWindowsAppSDKVersion` (override via `WinAppSdkVersionOverride`); WinUI/CommunityToolkit/NAudio pinned in the same file.
- **.NET SDK pin**: `global.json` pins the repo SDK version (currently `8.0.417`).
- **Model root defaults**: `backend/config/path_config.py` resolves `VOICESTUDIO_MODELS_PATH` (default `%PROGRAMDATA%\VoiceStudio\models`), and `backend/api/main.py` seeds HF/TTS/whisper/piper caches under that root.
- **Native tools**: ffmpeg can be overridden via `VOICESTUDIO_FFMPEG_PATH` (fallback PATH + common locations); shared DLLs can be injected for runtime engines via `VOICESTUDIO_FFMPEG_DLL_DIR` (XTTS runtime launcher uses `os.add_dll_directory` before loading torchcodec).
- **Operational reports**: `docs/reports/` (XAML diagnostics under `docs/reports/build/xaml/`).
- **Third-party binaries**: repo-local `third_party/whisper.cpp` is used when present for `whisper_cpp_engine.py` binary fallback.
- **Cursor MCP servers**: workspace MCP configuration lives in `cursor.mcp.json` with 24+ servers organized by category (Memory, Reasoning, Code Intelligence, Voice/Audio, Dev Tools). Key MCPs: `sequential-thinking` (structured reasoning), `openmemory`/`mem0` (persistent context), `chroma` (CodeRAG), `tree-sitter` (AST analysis), `context7` (library docs). See `docs/developer/MCP_OPTIMIZATION_GUIDE.md` for usage patterns.
- **Cursor preprompted baseline**: user rules live in `docs/developer/CURSOR_USER_RULES.md`; automation scripts `scripts/setup-preprompted-cursor.ps1` and `scripts/validate-cursor-setup.ps1`; reusable commands in `.cursor/commands/` (including `prompt-universal.md` and `role-*.md`).
- **Cursor rules expansion**: domain-scoped rules live in `.cursor/rules/domains/` and workflow modes in `.cursor/rules/workflows/` for role-aware guidance (architect dependency matrix + incident runbook references).
- **AI-native workflow rules**: new governance rules include dual-validation (`.cursor/rules/workflows/dual-validation.mdc`), model selection (`.cursor/rules/core/model-selection.mdc`), context strategy (`.cursor/rules/workflows/context-strategy.mdc`), and operational reliability (`.cursor/rules/workflows/operational-reliability.mdc`); ADR-009 documents the decision.
- **Platform identity**: ADR-010 establishes VoiceStudio as a native Windows installed application (WinUI 3 / Windows App SDK, installer-based distribution).
- **Role cheatsheet**: `docs/developer/ROLE_CHEATSHEET.md` provides role preprompts and one-liners for Cursor chats.
- **Error envelope enforcement**: backend error responses use `shared/schemas/error-envelope.schema.json`, taxonomy in `shared/contracts/error_codes.json`, and UI mapping via `ErrorMappingService`.
- **Rules integration reports**: `docs/reports/governance/RULES_GAP_ANALYSIS_REPORT.md` and `docs/reports/verification/RULES_VALIDATION_REPORT.md`.
- **Markdown linting**: repo-wide markdown normalization uses `markdownlint-cli2 --fix` with `.gitignore` to scope targets.
- **Bootstrap setup**: `scripts/bootstrap.ps1` creates venv, installs deps, optional engine deps, and runs verification.
- **Python lint/type config**: `ruff.toml` and `mypy.ini` define repo linting/type defaults.
- **Secret scanning config**: `.gitleaks.toml` adds allowlisted paths for gitleaks.
- **Dependency scan allowlist**: `config/pip-audit-ignore.txt` lists ignored vulnerability IDs for CI.
- **Publish status**: Gate C script `scripts/gatec-publish-launch.ps1` produces binlogs under `.buildlogs/`. Latest proof run (2026-01-27) is **green**: Release publish + UI smoke exits 0 with 11 nav steps and 0 binding failures (see `.buildlogs/gatec-latest.txt`, `.buildlogs/x64/Release/gatec-publish/ui_smoke_summary.json`).
- **Master Plan Phase 5 (Observability & Diagnostics)**: COMPLETE (2026-02-05). 15/15 tasks implemented: OpenTelemetry integration, trace propagation, trace visualization, engine tracing, SLO dashboard, Prometheus export, engine metrics, metrics retention, correlation filtering, diagnostic export, health aggregation, startup diagnostics, structured logging, error trends, user error messages. See `docs/reports/audit/PHASE5_OBSERVABILITY_AUDIT_2026-02-05.md`.
- **Gap Remediation Plan (2026-02-18)**: COMPLETE. 52 tasks across 5 sprints executed. Python lint errors reduced from 287 to 0. Empty catch violations resolved. Verification harness GREEN. Successor handoff created at `docs/governance/overseer/handoffs/OVERSEER_FINAL_HANDOFF.md`.
- **Baseline voice proof**: Task B complete via `.venv` + backend on 8001; proof at `.buildlogs/proof_runs/baseline_workflow_20260127-194335/proof_data.json` with SLO-6 MOS ≥ 3.5 and similarity ≥ 0.7 met.
- **Installer lane**: `installer/build-installer.ps1` publishes the frontend via Gate C (`scripts/gatec-publish-launch.ps1 -NoLaunch`) and Inno Setup packages from the Gate C publish directory (`installer/VoiceStudio.iss` via `MyAppSourceDir`).
- **Crash artifacts (Gate C)**:
  - `%LOCALAPPDATA%\VoiceStudio\crashes\latest.log` (managed unhandled exception pointer)
  - `%LOCALAPPDATA%\VoiceStudio\crashes\boot_latest.json` + `latest_startup_exception.log` (startup stage + pre-App exception pointer)
  - Native dumps (when enabled): `scripts/enable-wer-localdumps.ps1` → `%LOCALAPPDATA%\VoiceStudio\dumps\*.dmp`

## Gate status (A–H)

- **Gate A**: COMPLETE
- **Gate B**: COMPLETE
- **Gate C**: COMPLETE (VS-0012 UI smoke proof captured)
- **Gate D**: See `Recovery Plan/QUALITY_LEDGER.md`
- **Gate E**: See `Recovery Plan/QUALITY_LEDGER.md`
- **Gate F/G**: See `Recovery Plan/QUALITY_LEDGER.md`
- **Gate H**: COMPLETE (VS-0003 installer lifecycle proof captured)

## Key components and contracts

- **Speech-to-speech conversion (GAP-051, 2026-04-10)**: `SpeechToSpeechService.convert` — `AudioRegistry` source path, optional `*.pth` under target profile dir, `asyncio.to_thread` + `RVCEngine.convert_voice`, `create_audio_artifact_from_file`; route `POST /api/voice/sts/convert`; `IBackendClient.SynthesizeSpeechToSpeechAsync`; `ISpeechToSpeechService` + `SpeechToSpeechService` (App); `SpeechToSpeechView` / `SpeechToSpeechViewModel` — source `audio_id`, target `voice_profile_id`, convert, status, output; AutomationIds `SpeechToSpeechView_SourceAudioSelector`, `SpeechToSpeechView_TargetVoiceSelector`, `SpeechToSpeechView_ConvertButton`, `SpeechToSpeechView_StatusText`, `SpeechToSpeechView_OutputAudioLink`; `Gap051Tests` + `SpeechToSpeechViewModelSeamTests`; Python `test_speech_to_speech_service.py` (4). Closure: `docs/reports/verification/VOICESTUDIO_GAP051_SPEECH_TO_SPEECH_CONVERSION_LANE_CLOSURE_2026-04-10.md`.
- **STS consent / provenance gate (GAP-055, 2026-04-10)**: `SpeechToSpeechRequest` — `consent_acknowledged` + optional `consent_id`; `SpeechToSpeechService.convert` — `CONSENT_REQUIRED` (400) before source resolution, optional `SecurityService.consent` validation (403), post-success **consent audit** log (`speech_to_speech_consent_audit`); `SpeechToSpeechView` — `SpeechToSpeechView_ConsentCheckBox`; `SpeechToSpeechViewModel` — `ConsentAcknowledged` + `CanConvert` gate; `Gap055Tests` (5) + `SpeechToSpeechConsentSeamTests` (4); Python `test_speech_to_speech_service.py` (6). Closure: `docs/reports/verification/VOICESTUDIO_GAP055_VOICE_CONSENT_PROVENANCE_GATE_LANE_CLOSURE_2026-04-10.md`.
- **STS transformed-output disclosure (GAP-056, 2026-04-10)**: `SpeechToSpeechResponse` — `is_transformed`, `transformation_type`, `source_audio_id`, `disclosure_text`; `SpeechToSpeechService.convert` populates all four + passes `source=request.source_audio_id` to `create_audio_artifact_from_file`; **no audio watermark embedding** in this lane (deferred). `SpeechToSpeechViewModel` — `OutputIsTransformed`, `OutputDisclosureText`, `HasOutputDisclosure`; `SpeechToSpeechView` — `SpeechToSpeechView_DisclosureText`; `Gap056Tests` (6) + `SpeechToSpeechDisclosureSeamTests` (4); Python `test_speech_to_speech_service.py` (9). Closure: `docs/reports/verification/VOICESTUDIO_GAP056_TRANSFORMED_AUDIO_DISCLOSURE_LANE_CLOSURE_2026-04-10.md`.
- **STS durable metadata marking (GAP-056 slice 2, 2026-04-10)**: `write_provenance_sidecar` + `record_artifact_provenance_and_usage(transformation_meta=...)` + registry metadata `is_transformed` / `transformation_type`; `SpeechToSpeechService` passes transformation flags into `create_audio_artifact_from_file`; `StsMarkingStatus` + `GET /api/audio/{audio_id}/marking`; `POST /api/audio/export` adds `X-VoiceStudio-IsTransformed` / `X-VoiceStudio-TransformationType` when registered artifact metadata indicates transformation; `IBackendClient.GetStsMarkingAsync` / `ISpeechToSpeechService.GetMarkingAsync`; `SpeechToSpeechViewModel` `OutputMarkingVerified` / `OutputMarkingType`; `SpeechToSpeechView_MarkingBadge`; Python `test_sts_durable_marking.py` (6) + extended `test_speech_to_speech_service.py`; `Gap057Tests` (7) + `SpeechToSpeechMarkingSeamTests` (4). **No sample-level watermark embed** (deferred). Closure: `docs/reports/verification/VOICESTUDIO_GAP056_STS_DURABLE_MARKING_LANE_CLOSURE_2026-04-10.md`.
- **Long-form voice synthesis (GAP-049, 2026-04-10)**: `SynthesisService.synthesize_long_form` — sentence-boundary chunking (`TextPreprocessor.sentence_segmentation`), identical `VoiceSynthesizeRequest` prosody/settings per chunk, sequential `synthesize`, ordered `np.concatenate` + `create_audio_artifact_from_wav_array`, `partial_failure` + `failed_chunks`; route `POST /api/voice/synthesize/long-form`; WinUI `IVoiceSynthesisService.SynthesizeLongFormAsync` + `BackendClient`; `VoiceSynthesisView` / `VoiceSynthesisViewModel` — `UseLongForm`, `IsLongFormRunning`, `LongFormProgressText`, partial-failure toast; AutomationIds `VoiceSynthesisView_LongFormToggle`, `VoiceSynthesisView_LongFormProgressText`; `Gap049Tests` (6) + `VoiceSynthesisViewModelSeamTests` (5); Python `test_synthesis_service_long_form.py` (7). Closure: `docs/reports/verification/VOICESTUDIO_GAP049_LONG_FORM_VOICE_CONSISTENCY_LANE_CLOSURE_2026-04-10.md`.
- **Studio Sound one-click (GAP-048, 2026-04-10)**: `EffectsMixerView` / `EffectsMixerViewModel` — `RunStudioSoundCommand` builds curated **`StudioSoundEffects`** chain (**denoise → compressor → normalize**) with `GetDefaultParametersForEffectType`; **`IEffectChainClient.CreateEffectChainAsync`** → **`ProcessAudioWithChainAsync`** (`bypassChain: false`, `preview: false`) → **`DeleteEffectChainAsync`** (transient chain cleanup); session **`StudioSoundOutputAudioId`**; **`IsStudioSoundRunning`** + **`ProgressRing`**; AutomationId **`EffectsMixerView_StudioSoundButton`**; `Gap048Tests` (8) + 5 new `EffectsMixerViewModelSeamTests`. Closure: `docs/reports/verification/VOICESTUDIO_GAP048_ONE_CLICK_STUDIO_SOUND_LANE_CLOSURE_2026-04-10.md`.
- **Quality Benchmark side-by-side (GAP-052, 2026-04-09)**: `QualityBenchmarkView` / `QualityBenchmarkViewModel` — API-driven `ComparisonEngineOptions` (≥2 selected), `RunComparisonCommand` uses `IVoiceSynthesisService` as `_voiceSynthesisService` (field name satisfies `check_ibackendclient_creep.py`), parallel `ComparisonSlot` cards with `PlaySlotCommand` → `IAudioPlayerService.PlayBackendAudioIdAsync`, subjective `SubjectiveScore`, `SetPreferredEngineCommand`; session keys `QualityBenchmark.SelectedComparisonEnginesJson` + `QualityBenchmark.ComparisonTestText`; AutomationIds `QualityBenchmarkView_RunComparisonButton`, `QualityBenchmarkView_ComparisonSlots`; `Gap052Tests` (8) + extended seam tests. Closure: `docs/reports/verification/VOICESTUDIO_GAP052_ENGINE_BENCHMARKING_MOS_SIDEBYSIDE_LANE_CLOSURE_2026-04-09.md`.
- **Theme / min-sizes / contextual help (GAP-066, 2026-04-09)**: Design tokens `VSQ.Shell.MinWidth` (900) / `VSQ.Shell.MinHeight` (600) / `VSQ.PanelHost.MinWidth` (200) / `VSQ.HelpOverlay.BackgroundBrush` in `DesignTokens.xaml`; `MainWindow.xaml` root `RootGrid` binds to shell tokens; `PanelHost.xaml` binds `MinWidth`; `HelpOverlay.xaml` uses token (no raw hex); `FirstRunWizard` shows `HelpButton` on steps 3–4 with step-aware copy; `KeyboardCustomizationView` adds `HelpButton` with shortcut-capture help text; AutomationIds `FirstRunWizard_HelpButton` + `KeyboardCustomization_HelpButton`; `Gap066Tests` 8 source seam tests; `UnpackagedSettingsHelper` test-path seam (`UseTestSettingsPath`/`ResetSettingsPath`). Closure: `docs/reports/verification/VOICESTUDIO_GAP066_THEME_RESPONSIVE_MINSIZES_CONTEXTUAL_HELP_LANE_CLOSURE_2026-04-09.md`.
- **First-run wizard (GAP-063, 2026-04-09)**: `FirstRunWizard` **Window** — 5 steps (welcome + telemetry consent from **Loaded**, system check + GPU CPU-fallback panel, model readiness via `IModelManagerClient`, backend via `IDiagnosticsClient` + `IEnginesClient`, API keys + finish); `BackendProcessManager.EnsureBackendRunningAsync` for start; `OnboardingWizardService` singleton + `WizardCurrentStep` persistence; `App.xaml.cs` exits on cancel only for true first run (`isFirstRun && !WasCompleted`). Closure: `docs/reports/verification/VOICESTUDIO_GAP063_FIRST_RUN_WIZARD_MODELS_GPU_KEYS_LANE_CLOSURE_2026-04-09.md`.
- **Keyboard shortcut registry (GAP-065, 2026-04-09)**: `KeyboardShortcutService` + `IUnifiedKeyboardService` — defaults + `%AppData%\VoiceStudio\shortcuts.json` persistence; **Loaded** `InitializeAsync` from `MainWindow` (not ctor); **context-aware** `CheckForConflict` (same chord + same `ShortcutContext`); panels + `TransportShortcutCoordinator` use **`TryRegisterShortcut`**; customization: `KeyboardCustomizationView` / `KeyboardCustomizationViewModel`, **Keyboard Shortcuts** → **Customize…**, `ConflictDetected` → inline row badge. Closure: `docs/reports/verification/VOICESTUDIO_GAP065_SHORTCUT_REGISTRY_CONFLICTS_CUSTOMIZATION_LANE_CLOSURE_2026-04-09.md`.
- **Runtime proof gate (GAP-015 slice 1, 2026-04-08)**: Proof grades **S / I / R** documented in `docs/governance/TEST_CLASSIFICATION.md`; execution rows declare **Runtime proof requirement** in `docs/governance/EXECUTION_ROW_DISCIPLINE.md` §6. Optional bounded stack check: `scripts/verify.ps1 -RuntimeProof` (standalone) runs real golden-loop pytest + `tests/ci/test_runtime_proof_training_export.py`, writes `artifacts/verify/<ts>/runtime_proof.json`. Rolling verifier reports `runtime_proof_staleness` (FRESH/STALE/MISSING for `PROOF_GOLDEN_PATH_REAL_*.json`, 72h, warning-only). Closure: `docs/reports/verification/VOICESTUDIO_GAP015_PRODUCT_SLOS_AND_RUNTIME_PROOF_LANE_CLOSURE_2026-04-08.md`.
- **Runtime proof hard gate (GAP-015 slice 2, 2026-04-08)**: `runtime_proof.json` **schema v2** (PASS/FAIL/BLOCKED, commit hash, prerequisites, assertions); `scripts/ci/check_runtime_prerequisites.py` (engine-router probe redirects **stderr** so JSON parses under PowerShell `2>&1`); `python scripts/run_verification.py --enforce-runtime-proof` fails stale/missing proof; `verify.ps1 -EnforceRuntimeProof` wires enforce into Stage 9; default `run_verification.py` + full verify remain advisory for staleness. Closure + recorded proof in §4: `docs/reports/verification/VOICESTUDIO_GAP015_RUNTIME_PROOF_HARD_GATE_02_LANE_CLOSURE_2026-04-08.md`.
- **Percentile SLO baseline proof (GAP-015 slice 3, 2026-04-08)**: `verify.ps1 -RuntimeProof` emits sibling **`slo_baselines.json` schema v1** via `scripts/ci/write_slo_baseline_proof.py`; timing samples from `tests/ci/test_golden_loop_smoke_real.py` (health + synthesize) and `tests/ci/test_runtime_proof_training_export.py` (export rejection), appended through `tests/ci/slo_timing_io.py` when `VOICESTUDIO_SLO_TIMING_JSON` is set; `baseline_policy: advisory` (no CI threshold enforcement). Rolling verifier adds **`slo_baseline_freshness`** (72h advisory, never hard-fails). Closure: `docs/reports/verification/VOICESTUDIO_GAP015_PERCENTILE_SLO_BASELINES_03_LANE_CLOSURE_2026-04-08.md`; tracker **GAP-015** **Closed**.
- **Emotion preview authority (GAP-050, 2026-04-08)**: `POST /api/emotion/preview` in `backend/api/routes/emotion.py` uses the same pipeline as apply-extended (`resolve_emotion_prosody` → `apply_transform`) with `transform_context="emotion_preview"`; WinUI `IEmotionControlClient.PreviewEmotionAsync` returns `EmotionApplyExtendedResponse?` (shared shape with apply). Closure: `docs/reports/verification/VOICESTUDIO_GAP050_EMOTION_PREVIEW_AUTHORITY_LANE_CLOSURE_2026-04-08.md`.
- **Engine interface contract (Gate E)**: `VoiceStudio.Core.Engines` interfaces (`IEngine`, `ITextToSpeechEngine`, `ITranscriptionEngine`) + `EngineCapabilities`.
- **Schema validation (C#)**: `src/VoiceStudio.App/Utilities/SchemaValidationHelper.cs` validates settings and voice profiles against `shared/schemas/` using JsonSchema.Net; schemas are copied into app output under `shared/`.
- **IPC boundary**: `src/VoiceStudio.App/Services/IPC/NamedPipeClient.cs` validates IPC messages against `shared/schemas/ipc-message.schema.json` and uses `IIPCSerializer` (JSON default) with length-prefixed named pipe transport; error payloads include `request_id` + `path` for envelope alignment.
- **IPC transport update**: `MessagePackIpcSerializer` is now the default serializer in `AppServiceBootstrapper`, and the Python coordinator uses MessagePack payloads with length-prefix framing plus `job.progress`/`job.completed` events.
- **HF endpoint configuration**: `backend/api/main.py` reads `VOICESTUDIO_HF_ENDPOINT` and `VOICESTUDIO_HF_INFERENCE_API_BASE` for Hugging Face endpoints (fallback to router).
- **IPC schema enforcement (Python)**: `app/core/runtime/coordinator.py` validates inbound IPC messages against `shared/schemas/ipc-message.schema.json` and ensures error payloads match `shared/schemas/error-envelope.schema.json` before sending.
- **Frontend engine orchestration (Gate E)**: `src/VoiceStudio.App/Services/EngineManager.cs` with adapters in `src/VoiceStudio.App/Services/Engines/`.
- **Backend engine discovery/lifecycle**: `backend/api/routes/engines.py` (`/api/engines/*`).
- **Backend API versioning**: `/api/v1` is canonical with legacy `/api` deprecation headers set in `backend/api/main.py`.
- **Engine list contract**: `/api/engines/list` returns `engine_details` with manifest metadata; schemas in `shared/contracts/engine_descriptor.schema.json` and `shared/contracts/engine_list_response.schema.json`.
- **Job/quality contracts**: `shared/contracts/job_progress.schema.json` + `shared/contracts/quality_metrics.schema.json`; OpenAPI reflects engine list + job progress schemas.
- **Voice cloning quality metrics**: engine quality metrics pipeline uses ML prediction enablement for major voice cloning engines (tracked in VS-0009).
- **Profile management UI**: `src/VoiceStudio.App/Views/Panels/ProfilesView.xaml` + `ProfilesViewModel.cs` handle profile CRUD, preview, quality analysis triggers, import/export/duplicate, batch export, and drag-and-drop reorder using `/api/profiles`.
- **Visualization + training UI**: `LoudnessChartControl`/`RadarChartControl` render analyzer charts from `AnalyzerViewModel` data via XAML path geometry; `TrainingProgressChart` renders training quality history in `TrainingView` with a status filter driving job reloads.
- **Automation + ensemble UI**: `AutomationCurveEditorControl` renders curve points, `MacroNodeEditorControl` lists macro nodes/connections, and `EnsembleTimelineControl` renders voice blocks for `EnsembleSynthesisView` jobs.
- **A/B testing playback**: `ABTestingViewModel` plays sample audio streams via `IAudioPlayerService.PlayStreamAsync`.
- **Advanced visualization panels**: `AdvancedWaveformVisualizationView` and `AdvancedSpectrogramVisualizationView` now bind to their ViewModels (audio selection, config inputs, generate/compare actions) instead of placeholder panels.
- **Advanced real-time visualization**: `AdvancedRealTimeVisualizationView` exposes visualization type/preset controls with 3D/particle settings bound to `AdvancedRealTimeVisualizationViewModel`.
- **Advanced settings UI**: `AdvancedSettingsView` binds core UI/performance/audio/engine/system settings to `AdvancedSettingsViewModel` and uses NumberBox handlers for numeric updates.
- **Voice synthesis UI**: `VoiceSynthesisView` now surfaces profile/engine selection, text input, synth/play controls, and quality metrics display instead of placeholder content.
- **Timeline/Library layout**: `TimelineView` and `LibraryView` now have basic layout grids wiring existing controls (tracks list, scrub/playhead canvases, drag-drop overlay) instead of placeholder comments.
- **Analytics + collaboration controls**: `AnalyticsChartControl` renders metric line charts; `CollaborationIndicator` displays active users from `CollaborationService`.
- **MainWindow command deck**: `src/VoiceStudio.App/MainWindow.xaml` hosts a code-built MenuBar (File/Edit/View/Modules/Playback/Tools/AI/Help) via `MenuBarHost` in `MainWindow.xaml.cs` to avoid XAML MenuBar compiler issues.
- **Audio library refresh**: `AudioStore.RefreshAudioFilesAsync` aggregates project audio via `IBackendClient.ListProjectAudioAsync` across projects.
- **Emotion preset preview**: `EmotionStylePresetEditorViewModel` now synthesizes preset previews using backend profiles and plays audio via `IAudioPlayerService`.
- **Audio module guards**: `app/core/audio/__init__.py` now tolerates missing DSP dependencies by optional imports (SciPy/pyloudnorm/etc).

## Governance source of truth

- **Canonical registry**: `docs/governance/CANONICAL_REGISTRY.md` (updated 2026-01-25 with role system prompts)
- **Unified architecture plan**: `.cursor/plans/unified_architecture_index_*.plan.md` indexes all implemented components and establishes AI-driven development framework
- **Ledger**: `Recovery Plan/QUALITY_LEDGER.md` (canonical)
- **Service Level Objectives**: `docs/governance/SERVICE_LEVEL_OBJECTIVES.md` (SLOs for synthesis, transcription, API response, UI, engine availability, and quality)
- **Canonical roadmap (execution plan)**: `docs/governance/MASTER_ROADMAP_SUMMARY.md` (ledger remains status source of truth)
- **Master roadmap index**: `docs/governance/MASTER_ROADMAP_INDEX.md` (prompt execution navigation)
- **Prompt implementation guides (archived)**: `docs/archive/governance/prompts/` (PROMPT_04..12)
- **Roadmap alignment**: `docs/governance/MASTER_ROADMAP_SUMMARY.md` mirrors the PDF section order and includes completion criteria plus deep research sub-plans.
- **Planning suite (archived)**: overseer handoff plans under `docs/archive/governance/overseer/handoffs/VS-PLAN-*`.
- **Superseded roadmap archive**: legacy roadmaps are archived under `docs/archive/governance/roadmaps/` and excluded from Cursor indexing.
- **Subprocess isolation implementation guide**: `docs/design/ENGINE_SUBPROCESS_ISOLATION_IMPLEMENTATION_GUIDE.md` unifies runtime, lifecycle, and sandbox guidance.
- **External research integration**: ChatGPT + Copilot links are recorded in the master roadmap and plan suite; the Copilot baseline is treated as an external compatibility audit that must be reconciled against Lane A before any promotion.
- **Completion plan sources (archived)**: `docs/archive/governance/sources/plan_sources/`.
- **Roadmap development sources (archived)**: `docs/archive/governance/sources/roadmap_sources/`.
- **Governance archive policy**: `docs/governance/ARCHIVE_POLICY.md` (physical archive: `docs/archive/governance/`).
- **Change handoffs (archived)**: `docs/archive/governance/overseer/handoffs/` (proof runs + file lists)
- **Role task lists (archived)**: `docs/archive/governance/overseer/role_tasks/INDEX.md` (what each role does next)
- **Progression log (archived)**: `docs/archive/governance/overseer/PROJECT_PROGRESSION_LOG.md` (snapshot in same folder)
- **Production build plan (execution playbook)**: `docs/governance/VoiceStudio_Production_Build_Plan.md`
- **Role prompts + direction (archived)**: `docs/archive/governance/overseer/ALL_ROLE_PROMPTS.md`
- **Proof archive helper**: `scripts/archive-proof-artifacts.ps1` copies proof runs into `.buildlogs\\proof_runs`.
- **Repo organization map**: `docs/governance/PROJECT_ORGANIZATION_MAP.md`
- **Reorg log**: `docs/governance/PROJECT_REORG_LOG.md`
- **Finalization addendum tracking**: `docs/governance/MASTER_TASK_CHECKLIST.md` (Finalization Phase Addendum)
- **Risk register + gate map**: `docs/governance/RISK_REGISTER.md` and `docs/governance/PHASE_GATES_EVIDENCE_MAP.md`
- **Threat model**: `docs/reports/security/THREAT_MODEL.md` baseline security threat model.
- **Definition of Done**: `docs/governance/DEFINITION_OF_DONE.md` consolidates global, gate, and role DoD criteria.
- **QA evidence**: `docs/reports/verification/QA_EXECUTION_REPORT_2026-01-20.md` includes DSP-ready RVC pass (clears RVC skip)
- **Overseer role tasks**: `docs/governance/overseer/role_tasks/OVERSEER.md` now reflects canonical roadmap mapping completion.
- **Execution ownership**: Overseer covers roles 1-7; avoid worker labels in current planning docs.
- **Compatibility matrix (cu121)**: `docs/design/COMPATIBILITY_MATRIX.md` and `config/compatibility_matrix.yml` define the canonical baseline (torch/torchaudio 2.2.2+cu121, transformers 4.55.4, numpy 1.26.4). Historical compatibility docs are marked superseded.
- **Upgrade-lane XTTS proof status**: torchaudio 2.10.0+cu128 still triggers torchcodec load failures on Windows (WinError 127), so XTTS adds a `torchaudio.load` → `soundfile` fallback in `app/core/engines/xtts_engine.py`. Upgrade-lane proof succeeded at `.buildlogs\proof_runs\upgrade_lane_workflow_20260121-220357\proof_data.json` (see VS-0034).
- **Compatibility docs alignment**: Baseline is PyTorch 2.2.2+cu121 and Transformers 4.55.4; canonical source is `docs/design/COMPATIBILITY_MATRIX.md` and `config/compatibility_matrix.yml`.
- **Baseline stack**: Python 3.11.9 + Torch/Torchaudio 2.5.1+cu128 + Transformers 4.47.0; engines requiring newer stacks run in isolated venvs.
- **So-VITS proof prerequisite**: `scripts/sovits_svc_conversion_proof.py` expects `E:\VoiceStudio\models\checkpoints\Lain_SVC4\model.pth` + `config.json` (now populated from `therealvul/so-vits-svc-4.0` BaseModelv2 `G_202000.pth`). Conversion still needs an inference command (`SOVITS_SVC_INFER_COMMAND` or engine `infer_command`).

## 7-Role system prompts (2026-01-25)

- **Role system architecture**: VoiceStudio uses a 7-role system for professional software development: Role 0 (Overseer), Role 1 (System Architect), Role 2 (Build & Tooling), Role 3 (UI Engineer), Role 4 (Core Platform), Role 5 (Engine Engineer), Role 6 (Release Engineer).
- **Role prompts index**: `.cursor/prompts/ROLE_PROMPTS_INDEX.md` provides master navigation for all 7 role prompts.
- **Individual role prompts**: Each role has a comprehensive system prompt at `.cursor/prompts/ROLE_N_[NAME]_PROMPT.md` designed for AI agent role assumption.
- **Prompt structure**: All prompts include role identity, non-negotiables, required reading, current status, operational workflows, quality standards, tools/commands, role coordination, output format, and execution philosophy.
- **Usage pattern**: Reference role prompts with `@.cursor/prompts/ROLE_N_..._PROMPT.md` in Cursor chats for immediate role assumption.
- **Comprehensive guides**: Each prompt is a quick-start companion to detailed role guides at `docs/governance/roles/ROLE_N_*_GUIDE.md`.
- **Onboarding completed**: Full Overseer onboarding completed 2026-01-25, documented in `.cursor/prompts/ONBOARDING_COMPLETE_SUMMARY.md`.

## User Defined Namespaces

- Leave blank - user populates

## Backend voice cloning routes

- **Voice routes**: `backend/api/routes/voice.py`
  - Uses `_audio_storage` (`audio_id -> file_path`) to serve `/api/voice/audio/{audio_id}`.
  - `_audio_storage` is backed by a disk-backed registry (`backend/services/AudioArtifactRegistry.py`) so audio IDs survive backend restarts.
  - `/api/voice/clone` accepts optional `project_id` and will persist outputs to the project audio folder when provided.
  - Normalizes engine IDs; supports alias `xtts` -> `xtts_v2`.
  - Many engines write to `output_path` and return `None` (or `(None, metrics)`); treat a written file as
    success and still return a playable `audio_url`.
  - `/api/voice/clone` registers the generated file under a new `audio_id` and returns
    `/api/voice/audio/{audio_id}`.
  - XTTS quality metrics now include `voice_profile_match` (from reference audio) in
    `quality_metrics` alongside artifacts/clicks/distortion when available.
- **RVC conversion**: `backend/api/routes/rvc.py`
  - Normalizes So-VITS-SVC engine IDs (`sovits`, `sovits_v4`, `gpt_sovits` -> `sovits_svc`).
  - Pulls engine init kwargs from `EngineConfigService` (manifest-filtered), including `infer_command`.
  - Returns HTTP 424 when So-VITS-SVC inference is not configured (unless passthrough is enabled).
- **Wizard routes**: `backend/api/routes/voice_cloning_wizard.py`
  - Prefix: `/api/voice/clone/wizard` (used by
    `src/VoiceStudio.App/ViewModels/VoiceCloningWizardViewModel.cs`).
  - Wizard job state is persisted via `backend/services/JobStateStore.py` (no longer in-memory only).
  - Wizard UI displays parsed quality metrics in Step 4 (MOS/Similarity/Naturalness/SNR/Artifacts/Clicks/Distortion).
  - Wizard + quick clone ViewModels normalize profile names and use local copies of nullable inputs before API calls.
  - Real-time converter and quality optimization ViewModels now use local session/profile IDs for backend calls.
  - Wizard Step 4 binds quality metrics via a nested DataContext to avoid XamlCompiler failures on dotted bindings.

## Backend inspection + image sampler

- **Image sampler**: `backend/api/routes/img_sampler.py` now returns explicit HTTP errors when image generation
  is unavailable or output files are missing; it no longer emits fallback images.
- **Model inspection**: `backend/api/routes/model_inspect.py` now returns explicit status payloads when models
  or activations are unavailable (`model_available`, `activations_available`, `message`) and raises HTTP 503/500
  when the cache or inspection flow is unavailable.

## Key enforcement hooks

- **XAML toolchain**: wrapper/targets under `tools/` + MSBuild targets.
- **XAML compiler logging**: `tools/xaml-compiler-wrapper.cmd` only writes raw logs when
  `VSQ_XAML_RAW_LOG=1` and debug entries when `VSQ_XAML_DEBUG=1` to avoid log spam during
  design-time builds.
- **XAML wrapper delegation**: `tools/xaml-compiler-wrapper.cmd` now delegates to
  `tools/xaml-compiler-wrapper.ps1` for execution and logging.

## Engine notes

- **Runtime engine**: `app/core/runtime/runtime_engine.py` is a deprecation wrapper; `runtime_engine_enhanced.py` is the canonical implementation.
- **Coordinator runtime**: `app/core/runtime/coordinator.py` now loads engine/runtime manifests under `engines/`, uses `EnhancedRuntimeEngineManager` for start/stop/health, and surfaces runtime-capable engines via named pipe IPC.
- **So-VITS-SVC**: `app/core/engines/sovits_svc_engine.py` now supports external inference via
  `SOVITS_SVC_INFER_COMMAND` (or engine config `infer_command`) with optional `infer_workdir`
  and `allow_passthrough`.
- **So-VITS-SVC setup**: `docs/developer/SETUP.md` documents the inference command configuration options.
- **So-VITS-SVC cleanup**: conversion now tracks temporary input/output paths explicitly and logs cleanup
  failures instead of silently ignoring them.
- **So-VITS-SVC preflight**: `/api/health/preflight` now reports `sovits_svc` status alongside XTTS.
- **So-VITS-SVC proof**: `scripts/sovits_svc_conversion_proof.py` auto-resolves backend ports and blocks
  early when So-VITS-SVC inference is not configured.
- **So-VITS-SVC proof status**: `.buildlogs/proof_runs/sovits_svc_workflow_20260121-075330/proof_data.json`
  records a successful conversion using CPU inference (`vec768l12` in `models/checkpoints/Lain_SVC4/config.json`).
- **So-VITS-SVC GPU proof**: `.buildlogs/proof_runs/sovits_svc_workflow_20260121-081759/proof_data.json`
  records a CUDA conversion (`device=cuda`) with `env\\venv_sovits_svc` running torch `2.7.1+cu128`.
- **So-VITS-SVC encoder load**: `runtime/external/so-vits-svc/vencoder/ContentVec768L12.py` allowlists
  `fairseq.data.dictionary.Dictionary` via `torch.serialization.add_safe_globals` for torch 2.6+ weights-only load.
- **So-VITS-SVC proof diagnostics**: route checks now record OpenAPI/OPTIONS failures into
  `proof_data["config"]["route_check_error"]` for easier troubleshooting.
- **So-VITS-SVC tests**: `tests/unit/backend/api/routes/test_rvc.py` covers alias normalization and the
  inference-command guard (HTTP 424); `tests/unit/backend/services/test_model_preflight.py` asserts
  inference flags in preflight results.
- **Voice route preflight tests**: `tests/unit/backend/api/routes/test_voice.py` asserts XTTS alias
  normalization and validates TTS/VC preflight helpers.
- **Quality metrics tests**: `tests/unit/core/engines/test_additional_quality_metrics.py` verifies
  `missing_dependencies` includes actionable guidance when optional libs are absent.
- **Voice engines**: fallback `EngineProtocol` implementations in voice TTS engines now return
  explicit defaults instead of empty bodies to avoid incomplete method stubs.
- **Engine batch metrics**: engine batch helpers now log when performance metrics are unavailable
  instead of silently no-oping, and fallback `EngineProtocol` implementations in image engines
  return explicit defaults to avoid empty method stubs.
- **Voice cloning wizard**: wizard now supports quality mode selection and displays candidate metrics in Step 4.  

## Context management system

- **Session state**: `.cursor/STATE.md` tracks phase, active task, SSOT pointers, and proof index.
- **Task briefs**: `docs/tasks/` holds `TASK-####.md` briefs and the canonical template.
- **Hard gate + closure**: `.cursor/rules/workflows/state-gate.mdc` and `closure-protocol.mdc` enforce
  state read/acknowledgment and completion updates.
- **Verifier protocol**: `.cursor/rules/workflows/verifier-subagent.mdc` defines skeptical validation.
- **Lifecycle hooks**: `.cursor/hooks.json` invokes validation and audit scripts under `.cursor/hooks/`.
- **Completion guard**: `tools/overseer/verification/completion_guard.py` detects uncommitted completion markers; `scripts/run_verification.py` includes `completion_guard`, and the stop hook blocks closure when completion markers are uncommitted; audit logs include git metadata (2026-02-01).
- **Context allocator**: `tools/context/core/manager.py` assembles task-scoped bundles from STATE, task briefs, rules, optional OpenMemory, and git using `tools/context/config/context-sources.json` for weights/budgets; unit test at `tests/tools/test_context_allocator.py`.
- **OpenMemory reader**: `tools/context/sources/openmemory_reader.py` provides optional integration for context bundle assembly.
- **Context tests restored**: `tests/tools/test_context_source_adapters.py` and `tests/tools/test_context_allocator.py` are now present and passing (2026-01-29).
- **Context CLI restored**: `tools/context/cli/allocate.py` provides `python -m tools.context.cli.allocate --role <role> --preamble` (2026-01-29).
- **P.A.R.T. framework**: Context Manager implements P.A.R.T. structure (Prompt, Archive, Resources, Tools) with `ContextBundle.to_part_structure()` and `to_part_markdown()`; CLI `--part` flag for structured output (2026-01-30).
- **Progressive disclosure**: Tiered context loading with `ContextLevel` enum (HIGH: STATE+TASK, MID: +Brief+Ledger, LOW: all); allocator filters by level; CLI `--level high|mid|low` (2026-01-30).
- **MCP integration**: Context7, Linear, GitHub adapters in `tools/context/sources/` with env-gated graceful fallback; config updated with weights/budgets (2026-01-30).

## Agent governance tooling

- **Policy fragments**: `tools/overseer/agent/policies/` splits risk tiers and allowlists into
  `risk_tiers.yaml`, `path_allowlist.yaml`, and `tool_allowlist.yaml`; `base_policy.yaml` includes them via
  `include_files`.
- **Policy loader**: `tools/overseer/agent/policy_loader.py` now merges `include_files` fragments before
  validation.
- **Policy matching**: `tools/overseer/agent/policy_engine.py` expands env vars at match time and treats
  `**` path globs as recursive regex tokens to avoid false denials.
- **QueryDb tool**: `tools/overseer/agent/tools/db_tools.py` enforces parameterized SQLite queries only.
- **Approval store**: `tools/overseer/agent/approval_store.py` persists approval history in JSONL.
- **Agent UI**: `src/VoiceStudio.App/Views/AgentLogViewer.xaml` displays audit log entries from
  `%APPDATA%\\VoiceStudio\\logs\\agent_audit`.
- **Safe zones**: `tools/overseer/agent/safe_zones.py` deep-copies default safe zones per manager and
  matches recursive patterns using escaped regex handling.
- **Overseer CLI restored**: `tools/overseer/cli/gate_cli.py` and `ledger_cli.py` added; gate/ledger commands now run with expected reserved-ID warnings (2026-01-29).
- **Onboarding CLI restored**: `tools/onboarding/cli/onboard.py` generates onboarding packets; role registry auto-scans `.cursor/prompts/ROLE_*_PROMPT.md` and guides (2026-01-29).
- **Onboarding enhancements**: Packet validation (`_validate_packet_components`), structured logging for failures, Context Manager integration with `AllocationContext`, AgentRegistry with graceful degradation per ADR-015 (2026-01-30).
- **Debug Role (Role 7) architecture**: Clean Architecture domain layer in `tools/overseer/domain/` with entities (IssueReport, BugInvestigationSession), value objects (ResolutionLog, RootCause, ValidationResult), services (DebugWorkflow, RootCauseAnalyzer); 28 domain tests PASS; ADR-017 decision record (2026-01-30).
- **Role 7 documentation**: Comprehensive Debug Agent Guide (17 sections, 700+ lines) at `docs/governance/roles/ROLE_7_DEBUG_AGENT_GUIDE.md`; Integration Guide at `docs/developer/DEBUG_ROLE_INTEGRATION_GUIDE.md` with CLI reference, workflows, examples (2026-01-30).
- **HandoffQueue**: Cross-role issue escalation system at `tools/overseer/issues/handoff.py` with JSONL persistence; methods: handoff(), get_role_queue(), acknowledge(), complete() (2026-01-30).
- **Path config**: Enhanced `backend/config/path_config.py` with `get_ffmpeg_path()` (env → PATH → known dirs → bundled), `get_path(path_type)` supporting 7 types (models, ffmpeg, cache, checkpoints, logs, artifacts, data, config), `validate_path()`, comprehensive docstrings (2026-01-30).
- **Lifecycle hooks**: `hooks.json` config with beforeSubmitPrompt (state validation), afterFileEdit (audit), stop (closure reminder), sessionStart (role detection); scripts in `.cursor/hooks/` (2026-01-30).

## UI Testing Infrastructure (2026-02-13)

- **WinAppDriverSession class**: `tests/ui/conftest.py` contains a custom `WinAppDriverSession` class that makes direct HTTP requests to WinAppDriver, bypassing Selenium's WebDriver API entirely.
  - **Selenium 4.x Incompatibility**: Selenium 4.x uses W3C capabilities format, which WinAppDriver (JSON Wire Protocol) does not support. This caused `WebDriverException: Message: Bad capabilities` errors.
  - **Solution**: The `WinAppDriverSession` class directly POSTs session creation requests to WinAppDriver with the old-style capabilities format.
  - **Methods**: `find_element_by_*`, `click()`, `send_keys()`, `implicitly_wait()`, `quit()`.
- **UI Test Structure**:
  - `tests/ui/conftest.py`: Core fixtures (`winappdriver_service`, `driver`, `WinAppDriverSession`), app path discovery, WinAppDriver startup.
  - `tests/e2e/conftest.py`: E2E-specific fixtures, imports `WinAppDriverSession` from UI conftest.
  - `tests/ui/test_performance.py`, `test_accessibility.py`, `test_visual_regression.py`: All use `WinAppDriverSession` for driver fixture.
- **Application Path Discovery**: Test fixtures search multiple locations for `VoiceStudio.App.exe`:
  1. `VS_APP_PATH` environment variable (highest priority)
  2. `.buildlogs/x64/Debug/net8.0-windows10.0.19041.0/`
  3. `.buildlogs/publish/`
  4. `src/VoiceStudio.App/bin/x64/Debug/net8.0-windows10.0.19041.0/`
- **Prerequisites for UI Tests**:
  - WinAppDriver must be running (`C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe` on port 4723).
  - VoiceStudio app must be built (`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`).
  - Python deps: `pytest`, `selenium`, `websocket-client`, `Pillow`, `psutil`, `pytest-html`.
