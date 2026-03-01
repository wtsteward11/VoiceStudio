# Panel Registry Audit

**Date:** 2026-02-28
**Source:** CorePanelRegistrationService.cs + AdvancedPanelRegistrationService.cs
**Criteria:** View XAML exists, ViewModel CS exists, View > 10 lines (not a stub)

## Summary

| Metric | Count |
|--------|-------|
| Total panels registered | 47 |
| Fully implemented (View > 10 lines + ViewModel) | 36 |
| Stub views (View <= 10 lines) | 11 |
| Missing View files | 0 |
| Missing ViewModel files | 0 |

## Full Audit

| PanelId | Display Name | View Lines | Status |
|---------|-------------|-----------|--------|
| VoiceSynthesis | Voice Synthesis | 328 | OK |
| EnsembleSynthesis | Ensemble Synthesis | 384 | OK |
| BatchProcessing | Batch Processing | 11 | OK |
| TrainingDatasetEditor | Training Dataset Editor | 28 | OK |
| ModelManager | Model Manager | 28 | OK |
| Training | Training | 87 | OK |
| Transcribe | Transcribe | 457 | OK |
| Recording | Recording | 10 | STUB |
| AudioAnalysis | Audio Analysis | 10 | STUB |
| QualityControl | Quality Control | 470 | OK |
| Timeline | Timeline | 224 | OK |
| Profiles | Profiles | 347 | OK |
| Library | Library | 112 | OK |
| EffectsMixer | Effects Mixer | 574 | OK |
| Analyzer | Analyzer | 204 | OK |
| VoiceMorph | Voice Morph | 28 | OK |
| EmotionControl | Emotion Control | 28 | OK |
| Diagnostics | Diagnostics | 849 | OK |
| Settings | Settings | 679 | OK |
| Help | Help | 28 | OK |
| SSMLControl | SSML Control | 28 | OK |
| VoiceQuickClone | Quick Clone | 28 | OK |
| QualityDashboard | Quality Dashboard | 28 | OK |
| QualityBenchmark | Quality Benchmark | 28 | OK |
| ImageGen | Image Generation | 10 | STUB |
| VideoGen | Video Generation | 28 | OK |
| DeepfakeCreator | Deepfake Creator | 10 | STUB |
| DatasetQA | Dataset QA | 10 | STUB |
| ScriptEditor | Script Editor | 28 | OK |
| SceneBuilder | Scene Builder | 10 | STUB |
| Macro | Macro | 86 | OK |
| WorkflowAutomation | Workflow Automation | 10 | STUB |
| AdvancedSettings | Advanced Settings | 111 | OK |
| APIKeyManager | API Key Manager | 10 | STUB |
| GPUStatus | GPU Status | 28 | OK |
| TodoPanel | Todo Panel | 10 | STUB |
| text-speech-editor | Text Speech Editor | 28 | OK |
| prosody | Prosody & Phoneme Control | 28 | OK |
| spatial-audio | Spatial Audio | 28 | OK |
| ai-mixing-mastering | AI Mixing & Mastering | 18 | OK |
| voice-style-transfer | Voice Style Transfer | 10 | STUB |
| embedding-explorer | Speaker Embedding Explorer | 10 | STUB |
| ai-production-assistant | AI Production Assistant | 28 | OK |
| pronunciation-lexicon | Pronunciation Lexicon | 28 | OK |
| voice-morphing-blending | Voice Morphing/Blending | 28 | OK |
| plugin-gallery | Plugin Gallery | 407 | OK |
| theme-editor | Theme Editor | 246 | OK |

## Stub Panels (11)

These panels have minimal XAML (Grid + HelpOverlay only, 10 lines). They open without error but show no functional UI. All have corresponding ViewModels.

1. Recording
2. AudioAnalysis
3. ImageGen
4. DeepfakeCreator
5. DatasetQA
6. SceneBuilder
7. WorkflowAutomation
8. APIKeyManager
9. TodoPanel
10. voice-style-transfer
11. embedding-explorer

## v1.0 Scope Decision

Per V1_SCOPE.md, all 47 panels are in scope for v1.0. The 11 stub panels open without crash and display the panel title with a help overlay. They are accepted as "shell panels" for v1.0 -- they navigate, they don't crash, they show the panel identity. Expanding their UI to full functionality is deferred to v1.1.
