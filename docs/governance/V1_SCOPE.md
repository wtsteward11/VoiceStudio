# VoiceStudio v1.0 Scope Definition

**Created:** 2026-02-28
**Owner:** Overseer (Role 0)
**Purpose:** Freeze v1.0 completion scope so "done" has a concrete, testable definition.

---

## v1.0 Panel Set (48 panels)

Every panel listed below is registered in the panel registry, has a View (XAML) and ViewModel (C#), and must open without error. This is the complete v1.0 panel set.

### Core Panels (37) — CorePanelRegistrationService.cs

| # | PanelId | Display Name | Region |
|---|---------|-------------|--------|
| 1 | VoiceSynthesis | Voice Synthesis | Center |
| 2 | EnsembleSynthesis | Ensemble Synthesis | Center |
| 3 | BatchProcessing | Batch Processing | Center |
| 4 | TrainingDatasetEditor | Training Dataset Editor | Center |
| 5 | ModelManager | Model Manager | Center |
| 6 | Training | Training | Left |
| 7 | Transcribe | Transcribe | Center |
| 8 | Recording | Recording | Center |
| 9 | AudioAnalysis | Audio Analysis | Center |
| 10 | QualityControl | Quality Control | Right |
| 11 | Timeline | Timeline | Center |
| 12 | Profiles | Profiles | Left |
| 13 | Library | Library | Left |
| 14 | EffectsMixer | Effects Mixer | Right |
| 15 | Analyzer | Analyzer | Right |
| 16 | VoiceMorph | Voice Morph | Center |
| 17 | EmotionControl | Emotion Control | Right |
| 18 | Diagnostics | Diagnostics | Bottom |
| 19 | Settings | Settings | Right |
| 20 | Help | Help | Right |
| 21 | SSMLControl | SSML Control | Right |
| 22 | VoiceQuickClone | Quick Clone | Center |
| 23 | QualityDashboard | Quality Dashboard | Center |
| 24 | QualityBenchmark | Quality Benchmark | Center |
| 25 | ImageGen | Image Generation | Center |
| 26 | VideoGen | Video Generation | Center |
| 27 | DeepfakeCreator | Deepfake Creator | Center |
| 28 | DatasetQA | Dataset QA | Center |
| 29 | ScriptEditor | Script Editor | Center |
| 30 | SceneBuilder | Scene Builder | Center |
| 31 | Macro | Macro | Center |
| 32 | WorkflowAutomation | Workflow Automation | Center |
| 33 | AdvancedSettings | Advanced Settings | Right |
| 34 | APIKeyManager | API Key Manager | Right |
| 35 | GPUStatus | GPU Status | Right |
| 36 | TodoPanel | Todo Panel | Right |

### Advanced Panels (11) — AdvancedPanelRegistrationService.cs

| # | PanelId | Display Name | Region |
|---|---------|-------------|--------|
| 37 | text-speech-editor | Text Speech Editor | Center |
| 38 | prosody | Prosody & Phoneme Control | Center |
| 39 | spatial-audio | Spatial Audio | Right |
| 40 | ai-mixing-mastering | AI Mixing & Mastering | Right |
| 41 | voice-style-transfer | Voice Style Transfer | Center |
| 42 | embedding-explorer | Speaker Embedding Explorer | Right |
| 43 | ai-production-assistant | AI Production Assistant | Right |
| 44 | pronunciation-lexicon | Pronunciation Lexicon | Right |
| 45 | voice-morphing-blending | Voice Morphing/Blending | Center |
| 46 | plugin-gallery | Plugin Gallery | Center |
| 47 | theme-editor | Theme Editor | Right |

**Note:** Panel count is 47 (36 core + 11 advanced). The 48th slot was a count discrepancy from the initial investigation — 36 unique core panels are registered (TodoPanel is #36, not #37).

---

## v1.0 Golden Path Workflows (5 required E2E paths)

These workflows must pass end-to-end for v1.0 to be considered complete.

| # | Workflow | Steps | Acceptance |
|---|----------|-------|------------|
| 1 | **Import Audio** | Upload audio file via UI or `POST /api/audio/upload` | File appears in Library panel |
| 2 | **Transcribe** | Select audio, run transcription with any available engine | Transcription text returned, visible in UI |
| 3 | **Synthesize** | Enter text, select engine, run synthesis | Output audio artifact produced |
| 4 | **Export** | Save synthesized audio to disk | File saved, correct format, playable |
| 5 | **Quality Check** | View quality metrics for output | MOS/SNR/similarity scores visible |

---

## v1.0 Gate Requirements

| Gate | Requirement | Evidence |
|------|------------|---------|
| B (Build) | `dotnet build` 0 errors | Build log |
| C (Boot) | Release publish + UI smoke exit 0, binding failures empty | gatec artifacts |
| D (Storage) | Preflight 200, job persistence, artifact registry | API response + tests |
| E (Engine) | At least 1 engine completes synthesis E2E | Proof run with audio output |
| F (UI) | All 47 panels open without crash | Panel audit report |
| G (Test) | verify.ps1 PASS | Verification report |
| H (Installer) | Install/launch/upgrade/uninstall on clean VM | Lifecycle logs |

---

## Explicitly NOT in v1.0

The following are deferred to v1.1 or later:

- Multi-GPU scheduling and VRAM contention under concurrent load (mitigations in place, not production-hardened)
- Cloud/remote synthesis (local-first only)
- Mobile companion app
- Collaborative editing / multi-user
- Real-time voice conversion in production (prototype exists)
- Plugin marketplace (Plugin Gallery panel exists; marketplace backend does not)
- Automated model fine-tuning pipelines
- mypy strict compliance (5892 errors — accepted tech debt, VS-0043). mypy strict (`-StrictMypy`) is not a v1.0 gate; strict typing is deferred to v1.1.
- WhisperX diarization in production (engine adapter exists; dependency chain not validated on all platforms)

---

## Accepted Risks for v1.0

| Risk | Status | Mitigation |
|------|--------|-----------|
| RISK-001 Chatterbox torch incompatibility | Deferred | Separate venv when resolved |
| RISK-002 XAML compiler instability | Controlled | Wrapper with retry, SDK pinned |
| RISK-003 VRAM contention | Controlled | VRAM scheduler implemented (TD-013) |
| RISK-004 Python dep conflicts | Controlled | Venv families implemented (TD-015) |
| VS-0043 mypy strict | Accepted | Non-blocking, 5892 errors baselined |
| VS-0046 pip-audit CVEs | Accepted | All blocked by Python 3.9; upgrade to 3.10+ in v1.1 |

---

## Definition of Done Cross-Reference

This scope document satisfies the DEFINITION_OF_DONE.md requirement for "every planned panel" by defining exactly which 47 panels are planned. The DoD criteria map to:

| DoD Criterion | Scope Anchor |
|--------------|-------------|
| Windows Installer | Gate H |
| Pixel-Perfect UI | Design token compliance (all panels) |
| All Panels Functional | 47 panels listed above |
| No Placeholders/TODOs | 0 NotImplementedException in app code |
| Tested and Documented | verify.ps1 PASS + Golden Path |
