# Changelog

All notable changes to VoiceStudio Quantum+ will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.3] - 2026-02-24

### Reintegration Release

Reintegrated 154 commits from v1.0.0-baseline through v1.0.2 using a 7-phase validated process. Every phase verified with build, tests, and app launch before proceeding.

- **Backend**: Circuit breaker, engine adapters, plugin system, 46 API routes, domain model, data layer
- **Frontend**: 80+ panels, unified command system, design tokens, XAML reliability
- **Quality**: 1,017 C# tests, 135+ Python unit tests passing, ruff clean
- **Infrastructure**: Plugin sandbox, CI workflows, verification scripts, architecture docs
- **Security**: SQL parameterization, shell=True removal, RBAC, session management, encryption

See [docs/integration/delta-audit.md](docs/integration/delta-audit.md) for full reconciliation details.

---

## [1.0.0-rc2] - 2026-02-28

### All 47 Panels Functional

Built real XAML UI for all 11 previously-stub panels. Every panel now has functional controls binding to its ViewModel with backend integration.

#### Panels Built Out
- RecordingView: device selection, waveform display, record/stop/cancel
- AudioAnalysisView: spectral/temporal/perceptual analysis toggles, comparison
- ImageGenView: prompt editor, engine/dimensions/steps, generate/upscale
- DeepfakeCreatorView: engine selection, media type, consent, jobs list
- DatasetQAView: dataset selection, QA thresholds, clip results, cull
- SceneBuilderView: scene list, search, project, create/apply/delete
- WorkflowAutomationView: step editor, templates, save/test/run
- APIKeyManagerView: keys list with masking, add/validate/delete
- TodoPanelView: filters, todos list, create with priority/category/tags
- VoiceStyleTransferView: extract/analyze style, intensity slider, generate
- EmbeddingExplorerView: extract, compare, visualize, cluster, export

#### Evidence and Tooling
- Golden Path E2E: added export-to-disk step with WAV validation
- Support bundle: scripts/collect-support-bundle.ps1
- About dialog: third-party license link added
- Gate C: Release publish PASS (0 errors)
- Panel audit: 47/47 OK, 0 stubs

#### Verification
- Build: 0 errors (Debug and Release)
- Verification harness: 5/5 PASS
- No build config files modified (DO_NOT_CHANGE compliant)

---

## [1.0.0-rc1] - 2026-02-28

### Release Candidate 1

First release candidate for VoiceStudio v1.0.0. All gates GREEN, all governance registers reconciled, scope frozen.

#### Verified
- Build: 0 errors (Debug and Release)
- Gate C: Release publish PASS, EXE launches
- Verification harness: 5/5 PASS (gate_status, ledger_validate, completion_guard, empty_catch_check, xaml_safety_check)
- Panel registry: 47 panels (36 fully implemented, 11 shell panels accepted for v1.0)
- Pin alignment: 0 drift between implementation files and DO_NOT_CHANGE_BUILD_CONFIG.md
- Tech debt: 0 active items (TD-039 closed, all 39 items resolved)
- Risk register: 0 Open items (RISK-003/004 moved to Controlled)
- Dependency scanner: documented AllowedExceptions + WarnPrefixes for Microsoft.Data.*
- Pre-push hook: Verify-ResolvedPackages + Verify-XamlArtifacts gate before push
- Remote branches: pruned from 38 stale to 1 (origin/main only)

#### Added
- `docs/governance/V1_SCOPE.md` -- v1.0 scope freeze (47 panels, 5 Golden Path workflows, explicit not-in-v1.0 list)
- `docs/reports/audit/PANEL_REGISTRY_AUDIT.md` -- machine-verified panel inventory
- `docs/testing/GOLDEN_PATH_CHECKLIST.md` -- enhanced with fixtures, pass/fail criteria, time budgets
- `scripts/golden_path_e2e.ps1` -- automated Golden Path E2E runner
- `docs/testing/HOSTILE_ENVIRONMENT_TEST.md` -- hostile environment test protocol
- `docs/testing/INSTALLER_LIFECYCLE_PROTOCOL.md` -- Gate H lifecycle test protocol
- `docs/developer/QUICKSTART.md` -- fresh clone to running app guide
- `.githooks/pre-push` -- dependency and XAML artifact gates before push

#### Fixed
- `scripts/gatec-publish-launch.ps1` -- PRI source search with self-contained build cache fallback
- `scripts/verify.ps1` -- parse errors in report generation (here-string pipes, parentheses, Unicode)
- `tools/build/Verify-ResolvedPackages.ps1` -- documented AllowedExceptions + WarnPrefixes

#### Pending (user-action steps for GA)
- Gate H: installer lifecycle test on clean Windows 10/11 VMs
- Fresh clone build verification on clean machine
- Hostile environment test on non-dev machine

---

## [Unreleased]

### Added

#### Phase 11 Post-GA Polish (2026-02-21)
- **VS-0043 Type Safety**: Incremental mypy remediation on model_drift_detector, ab_testing, drift routes; MYPY_TRIAGE_PLAN updated with progress
- **Performance Baselines**: Inference benchmark section (cache, GPU, health) in PERFORMANCE_BASELINES.md
- **Sprint 1 Blocker Doc**: SPRINT1_GA_BLOCKER.md updated with Phase 11 note and SSH fallback

#### Phase 10 Production Readiness (2026-02-21)
- **Plugin Catalog**: pitch_shifter and silence_trimmer added to shared/catalog/plugins.json; 5 reference plugins; local catalog support via VOICESTUDIO_PLUGIN_CATALOG_URL
- **Model Registry**: Seeding from engines/config.json; 6 installed engines; activate/rollback; A/B experiment API
- **Operational Verification**: Alert rules (error_rate, latency_p95, circuit_open); metrics history; health aggregation; security and resilience test suites

#### Phase 8 Ecosystem Maturity (2026-02-21)
- **Model Lifecycle**: Model registry, baselines, rollback, A/B testing (ADR-043)
- **Observability**: Alert rules (config/alert_rules.json), metrics history, health summary
- **Debug.WriteLine Tier 2**: Bulk replacement; ErrorLogger recursion fix
- **Testing Hardening**: Security tests (injection, auth bypass, plugin escape); resilience tests (circuit breaker, backend crash, plugin isolation)

#### Phase 9 Final Launch Readiness (2026-02-21)
- **Model Data Drift Detection**: PSI-based statistical drift detection; `backend/services/model_drift_detector.py`, `/api/drift/status`, `/api/drift/history`, `/api/drift/baseline`; Diagnostics panel integration
- **User Documentation**: QUICK_START_GUIDE.md, FEATURE_GUIDE.md, TROUBLESHOOTING.md in docs/user/
- **Performance**: Inference benchmarks (tests/performance/test_inference_benchmarks.py), CUDA compatibility audit (docs/reports/CUDA_COMPATIBILITY_AUDIT.md)
- **Installer**: INSTALLER_LIFECYCLE_TEST_REPORT.md template for VM validation

#### Phase 7 Platform Operationalization (2026-02-21)
- **Plugin Marketplace**: Publisher registration, plugin submission workflow, review queue, ratings/reviews, download tracking
- **Operational Hardening**: API key persistence (JSON store), OTLP trace export (VOICESTUDIO_OTLP_ENDPOINT), Grafana dashboard config, health aggregation (plugins check), log rotation for .buildlogs/
- **Security Attestation**: Build provenance in release workflow, dependency audit report, SECURITY_CONTROLS_MATRIX.md, INCIDENT_RESPONSE_PLAYBOOK.md
- **Documentation**: DEPLOYMENT_TOPOLOGY.md, OPERATIONS_RUNBOOK.md, ADR index update (42 ADRs), architecture portfolio Phase 3–7 section

#### UI Testing Infrastructure Improvements (2026-02-13)
- Custom `WinAppDriverSession` class for Selenium 4.x/WinAppDriver compatibility
  - Direct HTTP requests to WinAppDriver (bypasses W3C capabilities format)
  - Unified driver fixture across all UI and E2E test files
- Enhanced application path discovery for UI tests
  - Multiple build output location search
  - `VS_APP_PATH` environment variable support
- UTF-8 encoding for test report generation
- Improved E2E test fixtures
  - `app_session` alias for `driver` fixture
  - Proper yield handling in `winappdriver_process` fixture

### Fixed

#### Phase 11 Type Safety (2026-02-21)
- model_drift_detector: Corrected _baselines/_current to dict[str, list[float]]; added _parse_list_float_dict for JSON load
- ab_testing: Fixed stats["variants"] append via typed variants list
- drift routes: Added return type annotations to all handlers

#### Phase 9 Quality Remediation (2026-02-21)
- Empty catch blocks: main.py (window format), model_baselines.py, model_registry.py (temp cleanup), PluginGateway.cs (HttpRequestException)
- python-multipart CVE (GHSA-wp53-j4wj-2cfg): bumped to >=0.0.22

#### UI Testing Infrastructure (2026-02-13)
- Selenium 4.x incompatibility with WinAppDriver JSON Wire Protocol
- Missing `app_session` fixture in E2E tests
- API endpoint path (`/api/engines/list` vs `/api/engine/list`)
- JSON compliance in error scenario tests (`float('inf')` → valid float)
- Test file encoding issues causing `UnicodeEncodeError`

#### Verification Infrastructure (2026-02-09)
- Unified verification harness (`scripts/verify.ps1`) with 8 stages
  - Clean Build (C#)
  - Python Quality Checks (ruff, mypy)
  - C# Unit Tests
  - Python Unit Tests
  - Contract Tests (C# ↔ Python)
  - Backend Integration Tests
  - UI Smoke Tests
  - Gate/Ledger Validation
- Change control rules (`docs/governance/CHANGE_CONTROL_RULES.md`)
  - Non-negotiable "no green = no merge" policy
  - Stabilization mode protocol
  - Cursor agent operating protocol
  - Blast radius limits (max files per change)
- AutomationId registry (`docs/developer/AUTOMATION_ID_REGISTRY.md`)
  - 50+ documented stable AutomationIds
  - Naming conventions and rules
  - Deprecation process
- Engine adapter contract tests (`tests/contract/test_engine_adapter_contracts.py`)
  - Protocol compliance verification
  - Method signature validation
  - Error handling contract tests
- Verification harness agent rule (`.cursor/rules/workflows/verification-harness.mdc`)

---

## [1.0.1] - 2026-02-05

### Added

#### Production Readiness
- Installer prerequisite detection for .NET 8 Desktop Runtime and Windows App SDK
- Python 3.10-3.12 detection with optional installation prompt
- Silent installation mode for enterprise deployment (`/VERYSILENT /SUPPRESSMSGBOXES`)
- Upgrade path validation with settings backup
- Uninstall cleanup for cache and log directories
- Crash recovery service with automatic session state restoration
- Opt-in error reporting with privacy controls
- Graceful degradation with circuit breaker pattern for engine failures
- Automatic and manual data backup with configurable retention
- UI virtualization for large lists (`IncrementalLoadingCollection`)
- Lazy panel loading for improved startup time
- Response caching for static API data
- Deferred service initialization for non-critical components

### Changed

- Improved installer upgrade flow with version comparison
- Enhanced engine service with fallback chain (XTTS → Chatterbox → Tortoise)
- Startup time optimized through deferred initialization

### Fixed

- XAML compiler issue with SLODashboardView.xaml UniformGrid namespace
- Ambiguous ColorHelper/Colors references in ViewModels
- Missing ErrorLogEntryViewModel properties (Context/ExceptionType)

---

## [1.0.0] - 2025-01-27

### Added

#### Voice Cloning & Synthesis
- XTTS v2 engine integration (Coqui TTS)
- Chatterbox TTS engine integration (Resemble AI)
- Tortoise TTS engine integration
- Voice profile management system
- Voice cloning from reference audio
- Text-to-speech synthesis
- Multi-language support (14-23 languages)
- Emotion control for voice synthesis
- Quality metrics system (MOS, similarity, naturalness, SNR, artifacts)
- Quality-based engine selection
- Quality enhancement pipeline

#### Timeline Editor
- Multi-track audio timeline
- Audio clip management
- Clip trimming and splitting
- Fade in/out controls
- Timeline scrubbing and playback
- Snap-to-grid editing
- Zoom and pan controls
- Time-based navigation

#### Effects & Processing
- Normalize effect
- Denoise effect
- Parametric EQ
- Compressor
- Reverb
- Delay
- Filter (high-pass, low-pass, band-pass)
- Chorus effect
- Pitch Correction
- Convolution Reverb
- Formant Shifter
- Distortion
- Multi-Band Processor
- Dynamic EQ
- Spectral Processor
- Granular Synthesizer
- Vocoder
- Effects chain editor
- Effect presets
- Parameter automation

#### Professional Mixer
- VU meters (real-time audio level monitoring)
- Fader controls (0.0-2.0 range)
- Pan controls
- Mute and solo buttons
- Send/return routing
- Sub-groups
- Master bus
- Mixer presets

#### Audio Analysis
- Waveform visualization
- Spectrogram analysis
- LUFS (Loudness Units Full Scale) metering
- Phase analysis
- Radar chart visualization
- Loudness analysis

#### Macro & Automation System
- Node-based macro editor
- Visual node editor with drag-and-drop
- Port-based connections
- Node types: Source, Processor, Control, Conditional, Output
- Automation curves editor
- Linear, step, and bezier interpolation
- Point manipulation (add, drag, delete)
- Parameter automation

#### Training Module
- Dataset management
- Training job control
- Progress tracking
- Model export/import

#### Batch Processing
- Queue-based batch processing
- Batch job creation and management
- Progress tracking
- Error handling

#### Transcription
- Whisper engine integration
- Speech-to-text transcription
- Word-level timestamps
- Diarization support
- Multi-language transcription

#### Projects
- Project management system
- Project organization
- Audio file storage
- Project metadata

#### Quality Improvement Features (IDEA 61-70)
- **Multi-Pass Synthesis (IDEA 61)**
  - Multiple refinement passes for maximum quality
  - Adaptive stopping when quality plateaus
  - Focus presets: Naturalness, Similarity, Artifact Reduction
  - Real-time quality tracking per pass
  - Automatic best pass selection

- **Reference Audio Pre-Processing (IDEA 62)**
  - Analyze and enhance reference audio before cloning
  - Automatic quality enhancement
  - Optimal segment selection
  - Quality analysis and recommendations
  - Dramatically improves cloning results

- **Artifact Removal (IDEA 63)**
  - Advanced detection and removal of audio artifacts
  - Supports clicks, pops, distortion, glitches, phase issues
  - Preview mode to analyze before applying
  - Comprehensive repair presets
  - Quality improvement tracking

- **Voice Characteristic Analysis (IDEA 64)**
  - Analyze pitch, formants, timbre, and prosody
  - Compare synthesized audio with reference
  - Similarity and preservation score calculation
  - Recommendations for quality improvement
  - Voice identity verification

- **Prosody Control (IDEA 65)**
  - Fine-tune prosody patterns and intonation
  - Intonation patterns: Rising, Falling, Flat
  - Custom pitch contour support
  - Word-level stress markers
  - Rhythm and tempo adjustment

- **Face Enhancement (IDEA 66)**
  - Enhance face quality in generated images and videos
  - Multi-stage enhancement for maximum quality
  - Presets: Portrait, Full Body, Close-Up
  - Face-specific algorithms
  - Quality analysis and improvement tracking

- **Temporal Consistency (IDEA 67)**
  - Enhance temporal consistency in video deepfakes
  - Reduce flickering and jitter
  - Configurable smoothing strength
  - Motion consistency enforcement
  - Temporal artifact detection

- **Training Data Optimization (IDEA 68)**
  - Analyze training dataset quality, diversity, and coverage
  - Select optimal samples for better training
  - Augmentation strategy suggestions
  - Quality improvement estimates
  - Optimized dataset creation

- **Real-Time Quality Preview (IDEA 69)**
  - Monitor quality metrics in real-time during processing
  - WebSocket-based quality updates
  - Multi-pass synthesis progress tracking
  - Post-processing stage-by-stage updates
  - Artifact detection progress
  - Quality trend analysis

- **Post-Processing Pipeline (IDEA 70)**
  - Multi-stage enhancement pipeline
  - Stages: Denoise, Normalize, Enhance, Repair
  - Automatic stage order optimization
  - Preview mode for all stages
  - Quality tracking per stage
  - Support for audio, image, and video

#### Backend API
- FastAPI backend with 164+ endpoints
- REST API for all operations
- WebSocket support for real-time updates (including quality preview)
- Quality improvement feature endpoints (9 new endpoints)
- Comprehensive error handling
- Rate limiting
- Request validation

#### Frontend Application
- WinUI 3 native Windows application
- MVVM architecture
- Modern UI with design system
- 6 core panels (Profiles, Timeline, Effects/Mixer, Analyzer, Macro, Diagnostics)
- Keyboard shortcuts
- Command palette
- Status bar
- Navigation rail
- Global Search (IDEA 5) - Search across all content types
- Context-Sensitive Action Bar (IDEA 2) - Quick actions in panel headers
- Enhanced Drag-and-Drop Visual Feedback (IDEA 4) - Visual feedback during drag operations
- Panel Resize Handles (IDEA 9) - Resize panels with visual feedback
- Contextual Right-Click Menus (IDEA 10) - Context-appropriate menus for interactive elements
- Toast Notification System (IDEA 11) - User-friendly notifications
- Multi-Select System (IDEA 12) - Multi-item selection with batch operations
- Undo/Redo Visual Indicator (IDEA 15) - Visual feedback for undo/redo operations
- Recent Projects Quick Access (IDEA 16) - Quick access to recently opened projects with pinning support (up to 10 recent, 3 pinned)

#### User Interface
- Global Search (IDEA 5) - Search across profiles, projects, audio files, markers, and scripts
- Context-Sensitive Action Bar (IDEA 2) - Quick actions in panel headers based on context
- Enhanced Drag-and-Drop Visual Feedback (IDEA 4) - Visual feedback during drag operations
- Panel Resize Handles (IDEA 9) - Resize panels with visual feedback
- Contextual Right-Click Menus (IDEA 10) - Context-appropriate menus for all interactive elements
- Toast Notification System (IDEA 11) - User-friendly notifications for success, errors, warnings, and info
- Multi-Select System (IDEA 12) - Select multiple items with visual indicators and batch operations
- Undo/Redo Visual Indicator (IDEA 15) - Visual feedback for undo/redo operations
- Recent Projects Quick Access (IDEA 16) - Quick access to recently opened projects with pinning support

#### Engine System
- Engine protocol interface
- Dynamic engine discovery via manifests
- Engine router system
- Engine lifecycle management
- Unlimited engine support (no hardcoded limits)

#### Documentation
- Complete user documentation
- Comprehensive API documentation (164+ endpoints)
- Quality features documentation (API, user, developer)
- Quality features tutorials (10 step-by-step guides)
- Quality features quick reference guide
- Quality features getting started guide
- Quality features troubleshooting guide
- Developer documentation
- Architecture documentation (including quality features architecture)
- Code structure documentation (including quality features)
- Contributing guide
- Setup guide
- Testing guide

#### Installer
- Windows installer (WiX and Inno Setup)
- Automatic dependency installation
- File associations (.voiceproj, .vprofile)
- Start Menu shortcuts
- Desktop shortcuts

#### Update System
- Automatic update checking
- Manual update check
- Update download with progress
- Update installation
- Release notes display

### Changed

- Initial release - no previous versions

### Deprecated

- None (initial release)

### Removed

- None (initial release)

### Fixed

- All known critical bugs addressed during development

### Security

- Local-first architecture (no cloud dependencies)
- Secure file handling
- Input validation
- Error handling without sensitive data exposure

---

## Version History

### [1.0.0] - 2025-01-27
- Initial stable release
- Complete voice cloning system
- Professional DAW-grade features
- Comprehensive documentation

---

## Future Releases

### Planned for v1.1.0
- Additional TTS engines (Higgs, F5-TTS, MaryTTS, Festival, eSpeak)
- Voice conversion engines (GPT-SoVITS, MockingBird)
- Audio-text alignment (Aeneas)
- Subtitle generation
- Enhanced UI panels
- Performance optimizations

### Planned for v1.2.0
- MCP integration
- AI-driven quality scoring
- AI-driven prosody tuning
- Advanced automation features
- Additional effects

### Planned for v2.0.0
- Cross-platform support
- Cloud sync (optional)
- Collaboration features
- Advanced AI features

---

## Release Types

- **Major Release (X.0.0):** Breaking changes, major new features
- **Minor Release (0.X.0):** New features, backward compatible
- **Patch Release (0.0.X):** Bug fixes, minor improvements

---

## Contributing

See [CONTRIBUTING.md](docs/developer/CONTRIBUTING.md) for guidelines on contributing to VoiceStudio Quantum+.

---

**Note:** This changelog is maintained manually. All changes are documented here.

