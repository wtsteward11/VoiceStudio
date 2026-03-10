# Gap Audit v6 — Reality Refresh

**Generated:** 2026-03-09  
**Updated:** 2026-03-10 (review fixes)  
**Refreshed:** 2026-03-10 (Step 1 — Reality Refresh)  
**Scope:** ViewModels, Views, Services — backend wiring, playback, request discipline, file pickers  
**Source:** Static scan of `src/VoiceStudio.App/`, `backend/api/route_registry.py`

**Review 2026-03-10:** Added Play button to TextSpeechEditorView.xaml; added InitializeWithWindow to FileSavePicker in TextSpeechEditorView ExportSessionAsync. Build passes.

**Step 1 Reality Refresh:** ProfilesTtl 30s (BackendClient.cs:236); 429→toast never modal (ErrorDialogService.cs:27-28, BaseViewModel.cs:138); localhost exempt for /api/profiles, /api/health (rate_limiting_enhanced.py:504-506). Non-exempt paths (engines, library, plugins) still hit 10 req/s → 429 toasts on startup.

---

## Top 5 Failure Patterns

| # | Pattern | Count | Impact |
|---|---------|-------|--------|
| 1 | **Request storms / 429 toasts** | Non-exempt paths | 429 now toast (FIXED); engines/library/plugins hit 10 req/s → 429 on startup; localhost exempt only profiles+health |
| 2 | **Missing playback wiring** | 11+ ViewModels | AudioId/AudioUrl produced but no PlayCommand; user cannot hear synthesis output |
| 3 | **Keystroke-triggered search without debounce** | 2 handlers | PluginGalleryView, ImageSearchView flood backend on keystroke; GlobalSearchViewModel FIXED |
| 4 | **File picker HWND init missing** | 24+ locations | FileSavePicker/FileOpenPicker may fail on unpackaged WinUI 3 |
| 5 | **Direct HttpClient usage** | 8+ locations | Bypasses BackendClient; no retry, no metrics, no rate-limit handling |

---

## 1. Backend Endpoint Strings — Route Alignment

Client endpoints are compared to `backend/api/route_registry.py`. Routes are registered via module names (e.g., `profiles` → `/api/profiles`). Custom paths in ViewModels may not match.

| File | Line | Endpoint | Status | Notes |
|------|------|----------|--------|------|
| HealthCheckViewModel.cs | 129, 135, 141, 147, 153 | /api/metrics/health, /api/health, /api/health/engines, /api/slo/health, /api/monitoring/health | OPEN | Some paths may 404; uses direct HttpClient |
| AdvancedRealTimeVisualizationViewModel.cs | 294, 329 | /api/visualization/get-data, /api/audio/playback-position | OPEN | Verify routes exist |
| SLODashboardViewModel.cs | 110 | /api/v1/diagnostics/slo | OPEN | v1 prefix may differ |
| DiagnosticsViewModel.cs | 1177 | /api/v1/diagnostics/traces | OPEN | v1 prefix |
| BackendClient.cs | 432+ | /api/profiles, /api/engines, etc. | FIXED | Canonical; matches route registry |
| ProfileGateway.cs | 26+ | /api/profiles/* | FIXED | Via BackendTransport |
| TimelineGateway.cs | 25+ | /api/projects/*/timeline | FIXED | Via BackendTransport |
| MixAssistantViewModel.cs | 229 | /api/mix-assistant/mix/analyze | OPEN | GAP-R5-HIGH-3: VM may call wrong path |
| EnsembleSynthesisViewModel.cs | 301, 347, 432, 548 | /api/ensemble | FIXED | Route exists |
| EmbeddingExplorerViewModel.cs | 209+ | /api/embedding-explorer/* | FIXED | Route exists |
| VoiceMorphingBlendingViewModel.cs | 226, 281, 350 | /api/voice-morph/* | FIXED | Route exists |
| TextBasedSpeechEditorViewModel.cs | 252+ | /api/transcribe, /api/edit/* | FIXED | Routes exist |
| TextHighlightingViewModel.cs | 180+ | /api/text-highlighting/* | FIXED | Route exists |
| ProsodyViewModel.cs | 142+ | /api/prosody/* | FIXED | Route exists |
| SpectrogramViewModel.cs | 151+ | /api/spectrogram/* | FIXED | Route exists |
| RealTimeAudioVisualizerViewModel.cs | 111+ | /api/realtime-visualizer/* | FIXED | Route exists |
| MultilingualSupportViewModel.cs | 115+ | /api/multilingual/* | FIXED | Route exists |
| ProfileComparisonViewModel.cs | 208, 226 | /api/voice/synthesize | FIXED | Route exists |
| SettingsViewModel.cs | 755 | /api/settings/check/dependencies | FIXED | Route exists |
| AudioPlayerService.cs | 386 | /api/audio/file/{id} | FIXED | Route exists |

---

## 2. Keystroke-Triggered Searches (No Debounce)

| File | Line | Handler | Backend Call | Status |
|------|------|---------|--------------|--------|
| PluginGalleryView.xaml.cs | 168 | SearchBox_TextChanged | SearchPluginsAsync → /api/plugins/search | OPEN |
| GlobalSearchViewModel.cs | 124 | OnSearchQueryChanged | SearchAsync (property change) | FIXED (300ms debounce) |
| ImageSearchView.xaml.cs | 88 | SearchBox_KeyDown | SearchCommand → image search | OPEN |
| PronunciationLexiconView.xaml.cs | 95 | SearchTextBox_KeyDown | SearchCommand on Enter | FIXED (Enter only) |
| VoiceSynthesisView.xaml.cs | 71 | TextInput_KeyDown | SynthesizeCommand on Enter | FIXED (Enter only) |
| PipelineConversationView.xaml.cs | 56 | OnInputKeyDown | SendMessageCommand | FIXED (Enter only) |
| AssistantView.xaml.cs | 120 | ChatInput_KeyDown | SendMessageCommand | FIXED (Enter only) |

**Fix:** Add 300ms debounce + CancellationToken for PluginGalleryView, GlobalSearchViewModel, ImageSearchView.

---

## 3. AudioId/AudioUrl Produced — No PlayCommand

| ViewModel | File | Property | PlayCommand | IAudioPlayerService | Status |
|-----------|------|----------|-------------|---------------------|--------|
| SSMLControlViewModel | SSMLControlViewModel.cs | PreviewAudioId | Yes | Yes | FIXED |
| VoiceSynthesisViewModel | VoiceSynthesisViewModel.cs | LastSynthesizedAudioId/Url | Partial | Yes | OPEN (PlayCommand for replay) |
| ProfileComparisonViewModel | ProfileComparisonViewModel.cs | AudioUrlA, AudioUrlB | No (uses PlayFileAsync in handler) | Yes | OPEN (standardize PlayCommand) |
| ABTestingViewModel | ABTestingViewModel.cs | SampleA/B.AudioUrl | No | Yes | OPEN |
| SpatialStageViewModel | SpatialStageViewModel.cs | SelectedAudioId | No | No | OPEN |
| VoiceMorphViewModel | VoiceMorphViewModel.cs | SelectedSourceAudioId | No | No | OPEN |
| TextSpeechEditorViewModel | TextSpeechEditorViewModel.cs | PreviewAudioId, PreviewAudioUrl | Yes | Yes | FIXED |
| TrainingDatasetEditorViewModel | TrainingDatasetEditorViewModel.cs | NewAudioId | No | No | OPEN |
| StyleTransferViewModel | StyleTransferViewModel.cs | SourceAudioId, OutputAudioId | No | No | OPEN |
| AudioAnalysisViewModel | AudioAnalysisViewModel.cs | SelectedAudioId, ReferenceAudioId | No | No | OPEN |
| AdvancedWaveformVisualizationViewModel | AdvancedWaveformVisualizationViewModel.cs | SelectedAudioId | No | No | OPEN |
| PronunciationLexiconViewModel | PronunciationLexiconViewModel.cs | TestAudioId, TestAudioUrl | No | No | OPEN |
| SpatialAudioViewModel | SpatialAudioViewModel.cs | AudioId, ProcessedAudioId/Url | No (has PreviewAudioCommand) | No | OPEN |
| VoiceMorphingBlendingViewModel | VoiceMorphingBlendingViewModel.cs | PreviewAudioId/Url, MorphedAudioId/Url | No | No | OPEN |
| VoiceBrowserViewModel | VoiceBrowserViewModel.cs | PreviewAudioId | No | No | OPEN |
| SonographyVisualizationViewModel | SonographyVisualizationViewModel.cs | SelectedAudioId | No | No | OPEN |
| AdvancedSpectrogramVisualizationViewModel | AdvancedSpectrogramVisualizationViewModel.cs | SelectedAudioId | No | No | OPEN |
| EmbeddingExplorerViewModel | EmbeddingExplorerViewModel.cs | SourceAudioId | No | No | OPEN |
| RecordingViewModel | RecordingViewModel.cs | RecordedAudioId, RecordedAudioUrl | Yes | Yes | FIXED |

---

## 4. Direct HttpClient Usage (Violations)

BackendClient is canonical. Direct HttpClient bypasses retry, metrics, rate-limit handling.

| File | Line | Usage | Status |
|------|------|-------|--------|
| ProfilesViewModel.cs | 1424 | Downloads preview audio from URL | OPEN |
| PluginGalleryView.xaml.cs | 25 | Plugin operations | OPEN |
| VoiceSynthesisViewModel.cs | 1114, 1761 | Downloads audio from URL | OPEN |
| StreamingAudioPlayer.cs | 536, 590 | Streams audio | OPEN (may be justified) |
| VideoGenView.xaml.cs | 247 | Video operations | OPEN |
| FirstRunWizard.xaml.cs | 282 | Engines check | OPEN |
| HealthCheckViewModel.cs | 101 | Health checks | OPEN |
| TimelineViewModel.cs | 920 | Timeline audio | OPEN |
| ImageGenView.xaml.cs | 250 | Image operations | OPEN |
| StartupDiagnostics.cs | 258, 312 | Startup checks | OPEN |
| MainWindow.Smoke.cs | 537 | Smoke test | FIXED (test only) |

**Excluded (infrastructure):** BackendClient, BackendProcessManager, BackendClientAdapter, CorrelationIdHandler, HmacSigningHandler, AppServices singleton, UpdateService, BackendConnectionMonitor.

---

## 5. File Picker — Missing InitializeWithWindow

WinUI 3 unpackaged: FileSavePicker/FileOpenPicker require `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` before `Show()`.

| File | Line | Picker Type | Status |
|------|------|-------------|--------|
| ProfilesViewModel.cs | 438, 560, 628 | FileOpenPicker, FileSavePicker (×2) | OPEN |
| DiagnosticsViewModel.cs | 968, 1294, 1625, 1686 | FileSavePicker (×4) | OPEN |
| TrainingView.xaml.cs | 644, 674 | FileSavePicker (×2) | OPEN |
| TextHighlightingViewModel.cs | 452, 550 | FileOpenPicker, FileSavePicker | OPEN |
| PronunciationLexiconViewModel.cs | 512 | FileSavePicker | OPEN |
| MultiVoiceGeneratorViewModel.cs | 347 | FileSavePicker | OPEN |
| QualityControlViewModel.cs | 882 | FileSavePicker | OPEN |
| TextSpeechEditorView.xaml.cs | 340 | FileSavePicker | FIXED (InitializeWithWindow) |
| WorkspaceManagerDialog.cs | 339, 364 | FileSavePicker, FileOpenPicker | OPEN |
| VoiceBrowserView.xaml.cs | 168 | FileSavePicker | OPEN |
| LexiconView.xaml.cs | 262 | FileSavePicker | OPEN |
| MultilingualSupportView.xaml.cs | 205 | FileSavePicker | OPEN |
| MultiVoiceGeneratorView.xaml.cs | 332 | FileSavePicker | OPEN |
| TemplateLibraryView.xaml.cs | 356 | FileSavePicker | OPEN |
| KeyboardShortcutsView.xaml.cs | 317 | FileSavePicker | OPEN |
| StyleTransferView.xaml.cs | 178 | FileSavePicker | OPEN |
| SceneBuilderView.xaml.cs | 349 | FileSavePicker | OPEN |
| AutomationView.xaml.cs | 259 | FileSavePicker | OPEN |
| PresetLibraryView.xaml.cs | 221 | FileSavePicker | OPEN |
| QualityBenchmarkView.xaml.cs | 116 | FileOpenPicker | OPEN |
| BackupRestoreViewModel.cs | 222 | FileSavePicker | OPEN |
| ModelManagerViewModel.cs | 335, 377 | FileSavePicker, FileOpenPicker | OPEN |
| ImageVideoEnhancementPipelineViewModel.cs | 350, 395 | FileOpenPicker (×2) | OPEN |
| DeepfakeCreatorView.xaml.cs | 255 | FileSavePicker | OPEN |
| UpscalingView.xaml.cs | 230 | FileSavePicker | OPEN |
| EmbeddingExplorerView.xaml.cs | 343, 373 | FileSavePicker (×2) | OPEN |
| SonographyVisualizationView.xaml.cs | 151 | FileSavePicker | OPEN |

---

## Summary

- **Request storms:** 429→toast (FIXED). ProfilesTtl 30s, single-flight cache (BackendClient.cs:236,774-819). Localhost exempt: /api/profiles, /api/health only. Engines/library/plugins need exemption or higher limit.
- **Playback wiring:** 11+ ViewModels need IAudioPlayerService + PlayCommand.
- **Debounce:** PluginGalleryView, ImageSearchView need 300ms debounce; GlobalSearchViewModel FIXED.
- **File pickers:** 24+ locations missing HWND init.
- **Direct HttpClient:** 8+ locations bypass BackendClient.
