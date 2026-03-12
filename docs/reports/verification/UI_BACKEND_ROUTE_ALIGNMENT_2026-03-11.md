# UI–Backend Route Alignment Report

Generated: 2026-03-11

| Prefix | Call Sites | Backend Provides | Decision |
|--------|------------|--------------------|----------|
| /api/advanced-settings | src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs:228, src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs:360, src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs:390 | yes | ok |
| /api/advanced-spectrogram | src/VoiceStudio.App/ViewModels/AdvancedSpectrogramVisualizationViewModel.cs:112, src/VoiceStudio.App/ViewModels/AdvancedSpectrogramVisualizationViewModel.cs:177, src/VoiceStudio.App/ViewModels/AdvancedSpectrogramVisualizationViewModel.cs:217 | yes | ok |
| /api/ai-enhancement | - | yes | ok |
| /api/analytics | src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs:131, src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs:168, src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs:205, src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs:242 | yes | ok |
| /api/api-keys | src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:183, src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:243, src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:305, src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:359, src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:398 (+1 more) | yes | ok |
| /api/articulation | - | yes | ok |
| /api/assistant | src/VoiceStudio.App/ViewModels/AIProductionAssistantViewModel.cs:216, src/VoiceStudio.App/ViewModels/AIProductionAssistantViewModel.cs:290, src/VoiceStudio.App/ViewModels/AIProductionAssistantViewModel.cs:337, src/VoiceStudio.App/ViewModels/AssistantViewModel.cs:178, src/VoiceStudio.App/ViewModels/AssistantViewModel.cs:244 (+3 more) | yes | ok |
| /api/audio | src/VoiceStudio.App/Services/BackendClient.cs:1044, src/VoiceStudio.App/Services/BackendClient.cs:1085, src/VoiceStudio.App/Services/BackendClient.cs:1103, src/VoiceStudio.App/Services/BackendClient.cs:1147, src/VoiceStudio.App/Services/BackendClient.cs:1194 (+20 more) | yes | ok |
| /api/audio-analysis | src/VoiceStudio.App/ViewModels/AudioAnalysisViewModel.cs:124, src/VoiceStudio.App/ViewModels/AudioAnalysisViewModel.cs:176, src/VoiceStudio.App/ViewModels/AudioAnalysisViewModel.cs:222 | yes | ok |
| /api/auth | src/VoiceStudio.App/Services/AuthService.cs:96, src/VoiceStudio.App/Services/AuthService.cs:166, src/VoiceStudio.App/Services/AuthService.cs:248 | yes | ok |
| /api/automation | src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:112, src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:161, src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:217, src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:292, src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:334 (+1 more) | yes | ok |
| /api/backup | src/VoiceStudio.App/Services/BackendClient.cs:3605, src/VoiceStudio.App/Services/BackendClient.cs:3621, src/VoiceStudio.App/Services/BackendClient.cs:3637, src/VoiceStudio.App/Services/BackendClient.cs:3653, src/VoiceStudio.App/Services/BackendClient.cs:3668 (+2 more) | yes | ok |
| /api/batch | src/VoiceStudio.App/Services/BackendClient.cs:2349, src/VoiceStudio.App/Services/BackendClient.cs:2365, src/VoiceStudio.App/Services/BackendClient.cs:2375, src/VoiceStudio.App/Services/BackendClient.cs:2393, src/VoiceStudio.App/Services/BackendClient.cs:2409 (+7 more) | yes | ok |
| /api/consent | - | yes | ok |
| /api/dataset | src/VoiceStudio.App/ViewModels/DatasetQAViewModel.cs:171, src/VoiceStudio.App/ViewModels/DatasetQAViewModel.cs:268 | yes | ok |
| /api/dataset-editor | src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:148, src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:206, src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:285, src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:361, src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:434 | yes | ok |
| /api/deepfake-creator | src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs:185, src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs:330, src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs:376, src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs:426 | yes | ok |
| /api/diagnostics | - | yes | ok |
| /api/drift | - | yes | ok |
| /api/dub | - | yes | ok |
| /api/edit | src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:283, src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:327, src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:410, src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:463, src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:509 (+6 more) | yes | ok |
| /api/effects | src/VoiceStudio.App/Services/BackendClient.cs:2183, src/VoiceStudio.App/Services/BackendClient.cs:2199, src/VoiceStudio.App/Services/BackendClient.cs:2217, src/VoiceStudio.App/Services/BackendClient.cs:2235, src/VoiceStudio.App/Services/BackendClient.cs:2251 (+4 more) | yes | ok |
| /api/embedding-explorer | src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:198, src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:264, src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:309, src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:357, src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:395 (+1 more) | yes | ok |
| /api/emotion | src/VoiceStudio.App/Services/BackendClient.cs:3900, src/VoiceStudio.App/Services/BackendClient.cs:3916, src/VoiceStudio.App/Services/BackendClient.cs:3934, src/VoiceStudio.App/Services/BackendClient.cs:3952, src/VoiceStudio.App/Services/BackendClient.cs:3968 (+7 more) | yes | ok |
| /api/emotion-style | src/VoiceStudio.App/ViewModels/EmotionStyleControlViewModel.cs:105, src/VoiceStudio.App/ViewModels/EmotionStyleControlViewModel.cs:142, src/VoiceStudio.App/ViewModels/EmotionStyleControlViewModel.cs:202 | yes | ok |
| /api/engine | src/VoiceStudio.App/Services/BackendClient.cs:2109 | yes | ok |
| /api/engines | src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:37, src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:47, src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:60, src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:86, src/VoiceStudio.App/Services/BackendClient.cs:3846 (+13 more) | yes | ok |
| /api/enhancement | src/VoiceStudio.App/Views/Panels/ImageVideoEnhancementPipelineViewModel.cs:321, src/VoiceStudio.App/Views/Panels/ImageVideoEnhancementPipelineViewModel.cs:428 | no | allowlisted (no backend) |
| /api/ensemble | src/VoiceStudio.App/Services/BackendClient.cs:2871, src/VoiceStudio.App/Services/BackendClient.cs:2887, src/VoiceStudio.App/ViewModels/EnsembleSynthesisViewModel.cs:302, src/VoiceStudio.App/ViewModels/EnsembleSynthesisViewModel.cs:348, src/VoiceStudio.App/ViewModels/EnsembleSynthesisViewModel.cs:433 (+1 more) | yes | ok |
| /api/errors | - | yes | ok |
| /api/eval | - | yes | ok |
| /api/experiments | - | yes | ok |
| /api/face-swap | - | yes | ok |
| /api/feedback | - | yes | ok |
| /api/formant | - | yes | ok |
| /api/gpu-status | src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs:192, src/VoiceStudio.App/ViewModels/GPUStatusViewModel.cs:143, src/VoiceStudio.App/Views/Panels/DiagnosticsView.xaml.cs:123 | yes | ok |
| /api/granular | - | yes | ok |
| /api/health | src/VoiceStudio.App/Services/BackendClient.cs:439, src/VoiceStudio.App/Services/BackendClient.cs:462, src/VoiceStudio.App/Services/BackendClient.cs:1616, src/VoiceStudio.App/Services/BackendConnectionMonitor.cs:146, src/VoiceStudio.App/Services/RequestMetricsService.cs:100 (+4 more) | yes | ok |
| /api/help | src/VoiceStudio.App/ViewModels/HelpViewModel.cs:123, src/VoiceStudio.App/ViewModels/HelpViewModel.cs:186, src/VoiceStudio.App/ViewModels/HelpViewModel.cs:235, src/VoiceStudio.App/ViewModels/HelpViewModel.cs:271, src/VoiceStudio.App/ViewModels/HelpViewModel.cs:308 | yes | ok |
| /api/huggingface-fix | - | yes | ok |
| /api/image | src/VoiceStudio.App/Views/Panels/ImageGenViewModel.cs:336, src/VoiceStudio.App/Views/Panels/ImageGenViewModel.cs:399 | yes | ok |
| /api/image-search | src/VoiceStudio.App/ViewModels/ImageSearchViewModel.cs:193, src/VoiceStudio.App/ViewModels/ImageSearchViewModel.cs:253, src/VoiceStudio.App/ViewModels/ImageSearchViewModel.cs:290, src/VoiceStudio.App/ViewModels/ImageSearchViewModel.cs:320, src/VoiceStudio.App/ViewModels/ImageSearchViewModel.cs:381 | yes | ok |
| /api/img | - | yes | ok |
| /api/instant-cloning | - | yes | ok |
| /api/integrations | - | yes | ok |
| /api/jobs | src/VoiceStudio.App/Services/Gateways/JobGateway.cs:26, src/VoiceStudio.App/Services/Gateways/JobGateway.cs:45, src/VoiceStudio.App/Services/Gateways/JobGateway.cs:54, src/VoiceStudio.App/Services/Gateways/JobGateway.cs:64, src/VoiceStudio.App/Services/Gateways/JobGateway.cs:73 (+10 more) | yes | ok |
| /api/lexicon | src/VoiceStudio.App/ViewModels/LexiconViewModel.cs:173, src/VoiceStudio.App/ViewModels/LexiconViewModel.cs:223, src/VoiceStudio.App/ViewModels/LexiconViewModel.cs:288, src/VoiceStudio.App/ViewModels/LexiconViewModel.cs:331, src/VoiceStudio.App/ViewModels/LexiconViewModel.cs:389 (+10 more) | yes | ok |
| /api/library | src/VoiceStudio.App/UseCases/LibraryUseCase.cs:28, src/VoiceStudio.App/UseCases/LibraryUseCase.cs:45, src/VoiceStudio.App/UseCases/LibraryUseCase.cs:72, src/VoiceStudio.App/UseCases/LibraryUseCase.cs:84, src/VoiceStudio.App/UseCases/LibraryUseCase.cs:95 (+13 more) | yes | ok |
| /api/lip-sync | - | yes | ok |
| /api/macros | src/VoiceStudio.App/Services/BackendClient.cs:1717, src/VoiceStudio.App/Services/BackendClient.cs:1738, src/VoiceStudio.App/Services/BackendClient.cs:1754, src/VoiceStudio.App/Services/BackendClient.cs:1770, src/VoiceStudio.App/Services/BackendClient.cs:1786 (+6 more) | yes | ok |
| /api/markers | src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:158, src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:222, src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:290, src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:331, src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:391 (+1 more) | yes | ok |
| /api/marketplace | - | yes | ok |
| /api/mcp | src/VoiceStudio.App/Services/BackendClient.cs:432 | no | allowlisted (no backend) |
| /api/mcp-dashboard | src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:165, src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:198, src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:232, src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:278, src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:332 (+4 more) | no | allowlisted (panel hidden) |
| /api/metrics | - | yes | ok |
| /api/mix-assistant | src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:229, src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:281, src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:326, src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:392, src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:444 (+6 more) | yes | ok |
| /api/mixer | src/VoiceStudio.App/Services/BackendClient.cs:2920, src/VoiceStudio.App/Services/BackendClient.cs:2936, src/VoiceStudio.App/Services/BackendClient.cs:2952, src/VoiceStudio.App/Services/BackendClient.cs:3025, src/VoiceStudio.App/Services/BackendClient.cs:3041 (+15 more) | yes | ok |
| /api/ml-optimization | - | yes | ok |
| /api/model | - | yes | ok |
| /api/models | src/VoiceStudio.App/Services/BackendClient.cs:2001, src/VoiceStudio.App/Services/BackendClient.cs:2022, src/VoiceStudio.App/Services/BackendClient.cs:2046, src/VoiceStudio.App/Services/BackendClient.cs:2062, src/VoiceStudio.App/Services/BackendClient.cs:2078 (+4 more) | yes | ok |
| /api/monitoring | - | yes | ok |
| /api/multi-speaker-dubbing | - | yes | ok |
| /api/multilingual | src/VoiceStudio.App/ViewModels/MultilingualSupportViewModel.cs:115, src/VoiceStudio.App/ViewModels/MultilingualSupportViewModel.cs:182, src/VoiceStudio.App/ViewModels/MultilingualSupportViewModel.cs:241 | yes | ok |
| /api/nr | - | yes | ok |
| /api/orchestrator | - | yes | ok |
| /api/pdf | - | yes | ok |
| /api/pipeline | src/VoiceStudio.App/Services/BackendClient.cs:4530, src/VoiceStudio.App/Services/BackendClient.cs:4540, src/VoiceStudio.App/Services/BackendClient.cs:4551 | yes | ok |
| /api/plugin-gallery | - | yes | ok |
| /api/plugins | - | yes | ok |
| /api/presets | src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:174, src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:242, src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:304, src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:346, src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:403 (+2 more) | yes | ok |
| /api/profiles | src/VoiceStudio.App/Core/Services/Generated/BackendClient.generated.cs:2820, src/VoiceStudio.App/Core/Services/Generated/BackendClient.generated.cs:2839, src/VoiceStudio.App/Services/BackendClient.cs:790, src/VoiceStudio.App/Services/BackendClient.cs:843, src/VoiceStudio.App/Services/BackendClient.cs:872 (+19 more) | yes | ok |
| /api/projects | src/VoiceStudio.App/Services/BackendClient.cs:936, src/VoiceStudio.App/Services/BackendClient.cs:964, src/VoiceStudio.App/Services/BackendClient.cs:989, src/VoiceStudio.App/Services/BackendClient.cs:1016, src/VoiceStudio.App/Services/BackendClient.cs:1035 (+33 more) | yes | ok |
| /api/prosody | src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:142, src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:194, src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:245, src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:284, src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:322 (+1 more) | yes | ok |
| /api/quality | src/VoiceStudio.App/Services/BackendClient.cs:3807, src/VoiceStudio.App/Services/BackendClient.cs:3813, src/VoiceStudio.App/Services/BackendClient.cs:3819, src/VoiceStudio.App/Services/BackendClient.cs:3824, src/VoiceStudio.App/Services/BackendClient.cs:3829 (+25 more) | yes | ok |
| /api/realtime-converter | src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:231, src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:307, src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:553, src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:617, src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:673 (+4 more) | yes | ok |
| /api/realtime-settings | - | yes | ok |
| /api/realtime-visualizer | src/VoiceStudio.App/ViewModels/RealTimeAudioVisualizerViewModel.cs:111, src/VoiceStudio.App/ViewModels/RealTimeAudioVisualizerViewModel.cs:152, src/VoiceStudio.App/ViewModels/RealTimeAudioVisualizerViewModel.cs:189 | yes | ok |
| /api/recording | src/VoiceStudio.App/ViewModels/RecordingViewModel.cs:401 | yes | ok |
| /api/repair | - | yes | ok |
| /api/rvc | - | yes | ok |
| /api/safety | - | yes | ok |
| /api/scenes | src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:123, src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:179, src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:254, src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:293, src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:355 | yes | ok |
| /api/script-editor | src/VoiceStudio.App/Services/BackendClient.cs:4475, src/VoiceStudio.App/Services/BackendClient.cs:4484, src/VoiceStudio.App/Services/BackendClient.cs:4490, src/VoiceStudio.App/Services/BackendClient.cs:4495, src/VoiceStudio.App/Services/BackendClient.cs:4500 (+3 more) | no | allowlisted (panel hidden) |
| /api/search | src/VoiceStudio.App/Services/BackendClient.cs:3890 | yes | ok |
| /api/settings | src/VoiceStudio.App/Services/BackendClient.cs:3726, src/VoiceStudio.App/Services/BackendClient.cs:3742, src/VoiceStudio.App/Services/BackendClient.cs:3757, src/VoiceStudio.App/Services/BackendClient.cs:3773, src/VoiceStudio.App/Services/BackendClient.cs:3789 (+6 more) | yes | ok |
| /api/shortcuts | src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:142, src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:202, src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:242, src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:279, src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:354 (+2 more) | yes | ok |
| /api/slo | - | yes | ok |
| /api/sonography | src/VoiceStudio.App/ViewModels/SonographyVisualizationViewModel.cs:135, src/VoiceStudio.App/ViewModels/SonographyVisualizationViewModel.cs:166, src/VoiceStudio.App/ViewModels/SonographyVisualizationViewModel.cs:192 | yes | ok |
| /api/spatial-audio | src/VoiceStudio.App/ViewModels/SpatialAudioViewModel.cs:129, src/VoiceStudio.App/ViewModels/SpatialAudioViewModel.cs:165, src/VoiceStudio.App/ViewModels/SpatialAudioViewModel.cs:217, src/VoiceStudio.App/ViewModels/SpatialAudioViewModel.cs:253, src/VoiceStudio.App/ViewModels/SpatialStageViewModel.cs:122 (+5 more) | yes | ok |
| /api/spectral | - | yes | ok |
| /api/spectrogram | src/VoiceStudio.App/ViewModels/SpectrogramViewModel.cs:151, src/VoiceStudio.App/ViewModels/SpectrogramViewModel.cs:221, src/VoiceStudio.App/ViewModels/SpectrogramViewModel.cs:263, src/VoiceStudio.App/ViewModels/SpectrogramViewModel.cs:305 | yes | ok |
| /api/ssml | src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:235, src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:291, src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:361, src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:406, src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:476 (+2 more) | yes | ok |
| /api/style-transfer | src/VoiceStudio.App/ViewModels/StyleTransferViewModel.cs:207, src/VoiceStudio.App/ViewModels/StyleTransferViewModel.cs:266, src/VoiceStudio.App/ViewModels/StyleTransferViewModel.cs:302, src/VoiceStudio.App/ViewModels/StyleTransferViewModel.cs:345, src/VoiceStudio.App/ViewModels/VoiceStyleTransferViewModel.cs:132 (+2 more) | yes | ok |
| /api/tags | src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:173, src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:234, src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:305, src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:347, src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:442 (+4 more) | yes | ok |
| /api/telemetry | - | yes | ok |
| /api/templates | src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:148, src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:222, src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:294, src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:333, src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:397 (+1 more) | yes | ok |
| /api/text-highlighting | src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:180, src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:234, src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:296, src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:343, src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:408 (+1 more) | no | allowlisted (panel hidden) |
| /api/timeline | src/VoiceStudio.App/UseCases/TimelineUseCase.cs:26, src/VoiceStudio.App/UseCases/TimelineUseCase.cs:39, src/VoiceStudio.App/UseCases/TimelineUseCase.cs:46, src/VoiceStudio.App/UseCases/TimelineUseCase.cs:53, src/VoiceStudio.App/UseCases/TimelineUseCase.cs:60 (+10 more) | yes | ok |
| /api/todo-panel | src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:257, src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:341, src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:420, src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:480, src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:514 (+2 more) | no | allowlisted (panel hidden) |
| /api/tracing | - | yes | ok |
| /api/training | src/VoiceStudio.App/Services/BackendClient.cs:2672, src/VoiceStudio.App/Services/BackendClient.cs:2688, src/VoiceStudio.App/Services/BackendClient.cs:2704, src/VoiceStudio.App/Services/BackendClient.cs:2720, src/VoiceStudio.App/Services/BackendClient.cs:2735 (+6 more) | yes | ok |
| /api/transcribe | src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:137, src/VoiceStudio.App/Services/BackendClient.cs:2549, src/VoiceStudio.App/Services/BackendClient.cs:2566, src/VoiceStudio.App/Services/BackendClient.cs:2582, src/VoiceStudio.App/Services/BackendClient.cs:2603 (+3 more) | yes | ok |
| /api/translation | - | yes | ok |
| /api/ultimate-dashboard | src/VoiceStudio.App/ViewModels/UltimateDashboardViewModel.cs:68 | no | allowlisted (panel hidden) |
| /api/upscaling | src/VoiceStudio.App/ViewModels/UpscalingViewModel.cs:188, src/VoiceStudio.App/ViewModels/UpscalingViewModel.cs:321, src/VoiceStudio.App/ViewModels/UpscalingViewModel.cs:344, src/VoiceStudio.App/ViewModels/UpscalingViewModel.cs:396 | yes | ok |
| /api/v1 | src/VoiceStudio.App/Views/Panels/DiagnosticsViewModel.cs:1177, src/VoiceStudio.App/Views/Panels/SLODashboardViewModel.cs:110 | no | allowlisted (no backend) |
| /api/v2 | - | yes | ok |
| /api/version | src/VoiceStudio.App/Services/BackendClient.cs:497, src/VoiceStudio.App/Services/BackendClient.cs:577 | yes | ok |
| /api/video | src/VoiceStudio.App/Services/BackendClient.cs:3302, src/VoiceStudio.App/Services/BackendClient.cs:3324, src/VoiceStudio.App/Services/BackendClient.cs:3343, src/VoiceStudio.App/Services/BackendClient.cs:3361, src/VoiceStudio.App/Services/BackendClient.cs:3399 (+3 more) | yes | ok |
| /api/visualization | src/VoiceStudio.App/Views/Panels/AdvancedRealTimeVisualizationViewModel.cs:294 | no | allowlisted (no backend) |
| /api/voice | src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:102, src/VoiceStudio.App/Services/BackendClient.cs:651, src/VoiceStudio.App/Services/BackendClient.cs:692, src/VoiceStudio.App/Services/BackendClient.cs:755, src/VoiceStudio.App/Services/BackendClient.cs:1047 (+23 more) | yes | ok |
| /api/voice-browser | src/VoiceStudio.App/ViewModels/VoiceBrowserViewModel.cs:164, src/VoiceStudio.App/ViewModels/VoiceBrowserViewModel.cs:205, src/VoiceStudio.App/ViewModels/VoiceBrowserViewModel.cs:235 | yes | ok |
| /api/voice-effects | - | yes | ok |
| /api/voice-morph | src/VoiceStudio.App/ViewModels/VoiceMorphingBlendingViewModel.cs:226, src/VoiceStudio.App/ViewModels/VoiceMorphingBlendingViewModel.cs:281, src/VoiceStudio.App/ViewModels/VoiceMorphingBlendingViewModel.cs:350, src/VoiceStudio.App/ViewModels/VoiceMorphViewModel.cs:165, src/VoiceStudio.App/ViewModels/VoiceMorphViewModel.cs:231 (+3 more) | yes | ok |
| /api/voice-speech | - | yes | ok |
| /api/waveform | src/VoiceStudio.App/ViewModels/AdvancedWaveformVisualizationViewModel.cs:202, src/VoiceStudio.App/ViewModels/AdvancedWaveformVisualizationViewModel.cs:257, src/VoiceStudio.App/ViewModels/AdvancedWaveformVisualizationViewModel.cs:297 | yes | ok |
| /api/workflows | src/VoiceStudio.App/Services/BackendClient.cs:1902, src/VoiceStudio.App/Services/BackendClient.cs:1920, src/VoiceStudio.App/Services/BackendClient.cs:1936, src/VoiceStudio.App/Services/BackendClient.cs:1952, src/VoiceStudio.App/Services/BackendClient.cs:1968 (+1 more) | yes | ok |

## Allowlist (archived + panel hidden)

- /api/mcp-dashboard
- /api/script-editor
- /api/text-highlighting
- /api/todo-panel
- /api/ultimate-dashboard
