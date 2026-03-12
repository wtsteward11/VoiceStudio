# UI–Backend Route Alignment Report

Generated: 2026-03-08

| Prefix | Call Sites | Backend Provides | Decision |
|--------|------------|--------------------|----------|
| /api/advanced-settings | src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs:228, src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs:360, src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs:390 | yes | ok |
| /api/advanced-spectrogram | src/VoiceStudio.App/ViewModels/AdvancedSpectrogramVisualizationViewModel.cs:112, src/VoiceStudio.App/ViewModels/AdvancedSpectrogramVisualizationViewModel.cs:177, src/VoiceStudio.App/ViewModels/AdvancedSpectrogramVisualizationViewModel.cs:217 | yes | ok |
| /api/ai-enhancement | - | yes | ok |
| /api/analytics | src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs:131, src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs:168, src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs:205, src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs:242 | yes | ok |
| /api/api-keys | src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:183, src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:243, src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:305, src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:359, src/VoiceStudio.App/ViewModels/APIKeyManagerViewModel.cs:398 (+1 more) | yes | ok |
| /api/articulation | - | yes | ok |
| /api/assistant | src/VoiceStudio.App/ViewModels/AIProductionAssistantViewModel.cs:216, src/VoiceStudio.App/ViewModels/AIProductionAssistantViewModel.cs:290, src/VoiceStudio.App/ViewModels/AIProductionAssistantViewModel.cs:337, src/VoiceStudio.App/ViewModels/AssistantViewModel.cs:178, src/VoiceStudio.App/ViewModels/AssistantViewModel.cs:244 (+3 more) | yes | ok |
| /api/audio | src/VoiceStudio.App/Services/BackendClient.cs:1014, src/VoiceStudio.App/Services/BackendClient.cs:1055, src/VoiceStudio.App/Services/BackendClient.cs:1073, src/VoiceStudio.App/Services/BackendClient.cs:1117, src/VoiceStudio.App/Services/BackendClient.cs:1164 (+18 more) | yes | ok |
| /api/audio-analysis | src/VoiceStudio.App/ViewModels/AudioAnalysisViewModel.cs:124, src/VoiceStudio.App/ViewModels/AudioAnalysisViewModel.cs:176, src/VoiceStudio.App/ViewModels/AudioAnalysisViewModel.cs:222 | yes | ok |
| /api/auth | src/VoiceStudio.App/Services/AuthService.cs:96, src/VoiceStudio.App/Services/AuthService.cs:166, src/VoiceStudio.App/Services/AuthService.cs:248 | yes | ok |
| /api/automation | src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:112, src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:161, src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:217, src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:292, src/VoiceStudio.App/ViewModels/AutomationViewModel.cs:334 (+1 more) | yes | ok |
| /api/backup | src/VoiceStudio.App/Services/BackendClient.cs:3575, src/VoiceStudio.App/Services/BackendClient.cs:3591, src/VoiceStudio.App/Services/BackendClient.cs:3607, src/VoiceStudio.App/Services/BackendClient.cs:3623, src/VoiceStudio.App/Services/BackendClient.cs:3638 (+2 more) | yes | ok |
| /api/batch | src/VoiceStudio.App/Services/BackendClient.cs:2319, src/VoiceStudio.App/Services/BackendClient.cs:2335, src/VoiceStudio.App/Services/BackendClient.cs:2345, src/VoiceStudio.App/Services/BackendClient.cs:2363, src/VoiceStudio.App/Services/BackendClient.cs:2379 (+7 more) | yes | ok |
| /api/consent | - | yes | ok |
| /api/dataset | src/VoiceStudio.App/ViewModels/DatasetQAViewModel.cs:171, src/VoiceStudio.App/ViewModels/DatasetQAViewModel.cs:268 | yes | ok |
| /api/dataset-editor | src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:148, src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:206, src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:285, src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:361, src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs:434 | yes | ok |
| /api/deepfake-creator | src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs:185, src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs:330, src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs:376, src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs:426 | yes | ok |
| /api/diagnostics | - | yes | ok |
| /api/drift | - | yes | ok |
| /api/dub | - | yes | ok |
| /api/edit | src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:281, src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:325, src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:408, src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:461, src/VoiceStudio.App/ViewModels/TextBasedSpeechEditorViewModel.cs:507 (+6 more) | yes | ok |
| /api/effects | src/VoiceStudio.App/Services/BackendClient.cs:2153, src/VoiceStudio.App/Services/BackendClient.cs:2169, src/VoiceStudio.App/Services/BackendClient.cs:2187, src/VoiceStudio.App/Services/BackendClient.cs:2205, src/VoiceStudio.App/Services/BackendClient.cs:2221 (+4 more) | yes | ok |
| /api/embedding-explorer | src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:209, src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:275, src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:320, src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:368, src/VoiceStudio.App/ViewModels/EmbeddingExplorerViewModel.cs:406 (+1 more) | yes | ok |
| /api/emotion | src/VoiceStudio.App/Services/BackendClient.cs:3860, src/VoiceStudio.App/Services/BackendClient.cs:3876, src/VoiceStudio.App/Services/BackendClient.cs:3894, src/VoiceStudio.App/Services/BackendClient.cs:3912, src/VoiceStudio.App/Services/BackendClient.cs:3928 (+7 more) | yes | ok |
| /api/emotion-style | src/VoiceStudio.App/ViewModels/EmotionStyleControlViewModel.cs:105, src/VoiceStudio.App/ViewModels/EmotionStyleControlViewModel.cs:142, src/VoiceStudio.App/ViewModels/EmotionStyleControlViewModel.cs:202 | yes | ok |
| /api/engine | src/VoiceStudio.App/Services/BackendClient.cs:2079 | yes | ok |
| /api/engines | src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:37, src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:47, src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:60, src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:86, src/VoiceStudio.App/Services/BackendClient.cs:3806 (+11 more) | yes | ok |
| /api/enhancement | src/VoiceStudio.App/Views/Panels/ImageVideoEnhancementPipelineViewModel.cs:321, src/VoiceStudio.App/Views/Panels/ImageVideoEnhancementPipelineViewModel.cs:428 | no | allowlisted (no backend) |
| /api/ensemble | src/VoiceStudio.App/Services/BackendClient.cs:2841, src/VoiceStudio.App/Services/BackendClient.cs:2857, src/VoiceStudio.App/ViewModels/EnsembleSynthesisViewModel.cs:301, src/VoiceStudio.App/ViewModels/EnsembleSynthesisViewModel.cs:347, src/VoiceStudio.App/ViewModels/EnsembleSynthesisViewModel.cs:432 (+1 more) | yes | ok |
| /api/errors | - | yes | ok |
| /api/eval | - | yes | ok |
| /api/experiments | - | yes | ok |
| /api/face-swap | - | yes | ok |
| /api/feedback | - | yes | ok |
| /api/formant | - | yes | ok |
| /api/gpu-status | src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs:192, src/VoiceStudio.App/ViewModels/GPUStatusViewModel.cs:143, src/VoiceStudio.App/Views/Panels/DiagnosticsView.xaml.cs:123 | yes | ok |
| /api/granular | - | yes | ok |
| /api/health | src/VoiceStudio.App/Services/BackendClient.cs:429, src/VoiceStudio.App/Services/BackendClient.cs:452, src/VoiceStudio.App/Services/BackendClient.cs:1586, src/VoiceStudio.App/Services/BackendConnectionMonitor.cs:136, src/VoiceStudio.App/Services/Gateways/BackendTransport.cs:394 (+2 more) | yes | ok |
| /api/help | src/VoiceStudio.App/ViewModels/HelpViewModel.cs:120, src/VoiceStudio.App/ViewModels/HelpViewModel.cs:183, src/VoiceStudio.App/ViewModels/HelpViewModel.cs:232, src/VoiceStudio.App/ViewModels/HelpViewModel.cs:268, src/VoiceStudio.App/ViewModels/HelpViewModel.cs:305 | yes | ok |
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
| /api/macros | src/VoiceStudio.App/Services/BackendClient.cs:1687, src/VoiceStudio.App/Services/BackendClient.cs:1708, src/VoiceStudio.App/Services/BackendClient.cs:1724, src/VoiceStudio.App/Services/BackendClient.cs:1740, src/VoiceStudio.App/Services/BackendClient.cs:1756 (+6 more) | yes | ok |
| /api/markers | src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:158, src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:222, src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:290, src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:331, src/VoiceStudio.App/ViewModels/MarkerManagerViewModel.cs:391 (+1 more) | yes | ok |
| /api/marketplace | - | yes | ok |
| /api/mcp | src/VoiceStudio.App/Services/BackendClient.cs:422 | no | allowlisted (no backend) |
| /api/mcp-dashboard | src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:165, src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:198, src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:232, src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:278, src/VoiceStudio.App/ViewModels/MCPDashboardViewModel.cs:332 (+4 more) | no | allowlisted (panel hidden) |
| /api/metrics | - | yes | ok |
| /api/mix-assistant | src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:229, src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:281, src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:326, src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:392, src/VoiceStudio.App/ViewModels/AIMixingMasteringViewModel.cs:444 (+6 more) | yes | ok |
| /api/mixer | src/VoiceStudio.App/Services/BackendClient.cs:2890, src/VoiceStudio.App/Services/BackendClient.cs:2906, src/VoiceStudio.App/Services/BackendClient.cs:2922, src/VoiceStudio.App/Services/BackendClient.cs:2995, src/VoiceStudio.App/Services/BackendClient.cs:3011 (+15 more) | yes | ok |
| /api/ml-optimization | - | yes | ok |
| /api/model | - | yes | ok |
| /api/models | src/VoiceStudio.App/Services/BackendClient.cs:1971, src/VoiceStudio.App/Services/BackendClient.cs:1992, src/VoiceStudio.App/Services/BackendClient.cs:2016, src/VoiceStudio.App/Services/BackendClient.cs:2032, src/VoiceStudio.App/Services/BackendClient.cs:2048 (+4 more) | yes | ok |
| /api/monitoring | - | yes | ok |
| /api/multi-speaker-dubbing | - | yes | ok |
| /api/multilingual | src/VoiceStudio.App/ViewModels/MultilingualSupportViewModel.cs:115, src/VoiceStudio.App/ViewModels/MultilingualSupportViewModel.cs:182, src/VoiceStudio.App/ViewModels/MultilingualSupportViewModel.cs:241 | yes | ok |
| /api/nr | - | yes | ok |
| /api/orchestrator | - | yes | ok |
| /api/pdf | - | yes | ok |
| /api/pipeline | src/VoiceStudio.App/Services/BackendClient.cs:4490, src/VoiceStudio.App/Services/BackendClient.cs:4500, src/VoiceStudio.App/Services/BackendClient.cs:4511 | yes | ok |
| /api/plugin-gallery | - | yes | ok |
| /api/plugins | - | yes | ok |
| /api/presets | src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:171, src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:239, src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:301, src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:343, src/VoiceStudio.App/ViewModels/PresetLibraryViewModel.cs:400 (+2 more) | yes | ok |
| /api/profiles | src/VoiceStudio.App/Core/Services/Generated/BackendClient.generated.cs:2820, src/VoiceStudio.App/Core/Services/Generated/BackendClient.generated.cs:2839, src/VoiceStudio.App/Services/BackendClient.cs:768, src/VoiceStudio.App/Services/BackendClient.cs:820, src/VoiceStudio.App/Services/BackendClient.cs:849 (+17 more) | yes | ok |
| /api/projects | src/VoiceStudio.App/Services/BackendClient.cs:906, src/VoiceStudio.App/Services/BackendClient.cs:934, src/VoiceStudio.App/Services/BackendClient.cs:959, src/VoiceStudio.App/Services/BackendClient.cs:986, src/VoiceStudio.App/Services/BackendClient.cs:1005 (+33 more) | yes | ok |
| /api/prosody | src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:142, src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:194, src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:245, src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:284, src/VoiceStudio.App/ViewModels/ProsodyViewModel.cs:322 (+1 more) | yes | ok |
| /api/quality | src/VoiceStudio.App/Services/BackendClient.cs:3777, src/VoiceStudio.App/Services/BackendClient.cs:3783, src/VoiceStudio.App/Services/BackendClient.cs:3789, src/VoiceStudio.App/Services/BackendClient.cs:3794, src/VoiceStudio.App/Services/BackendClient.cs:3799 (+25 more) | yes | ok |
| /api/realtime-converter | src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:231, src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:307, src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:553, src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:617, src/VoiceStudio.App/ViewModels/RealTimeVoiceConverterViewModel.cs:673 (+4 more) | yes | ok |
| /api/realtime-settings | - | yes | ok |
| /api/realtime-visualizer | src/VoiceStudio.App/ViewModels/RealTimeAudioVisualizerViewModel.cs:111, src/VoiceStudio.App/ViewModels/RealTimeAudioVisualizerViewModel.cs:152, src/VoiceStudio.App/ViewModels/RealTimeAudioVisualizerViewModel.cs:189 | yes | ok |
| /api/recording | src/VoiceStudio.App/ViewModels/RecordingViewModel.cs:351 | yes | ok |
| /api/repair | - | yes | ok |
| /api/rvc | - | yes | ok |
| /api/safety | - | yes | ok |
| /api/scenes | src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:120, src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:176, src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:251, src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:290, src/VoiceStudio.App/ViewModels/SceneBuilderViewModel.cs:352 | yes | ok |
| /api/script-editor | src/VoiceStudio.App/Services/BackendClient.cs:4435, src/VoiceStudio.App/Services/BackendClient.cs:4444, src/VoiceStudio.App/Services/BackendClient.cs:4450, src/VoiceStudio.App/Services/BackendClient.cs:4455, src/VoiceStudio.App/Services/BackendClient.cs:4460 (+3 more) | no | allowlisted (panel hidden) |
| /api/search | src/VoiceStudio.App/Services/BackendClient.cs:3850 | yes | ok |
| /api/settings | src/VoiceStudio.App/Services/BackendClient.cs:3696, src/VoiceStudio.App/Services/BackendClient.cs:3712, src/VoiceStudio.App/Services/BackendClient.cs:3727, src/VoiceStudio.App/Services/BackendClient.cs:3743, src/VoiceStudio.App/Services/BackendClient.cs:3759 (+6 more) | yes | ok |
| /api/shortcuts | src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:139, src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:199, src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:239, src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:276, src/VoiceStudio.App/ViewModels/KeyboardShortcutsViewModel.cs:351 (+2 more) | yes | ok |
| /api/slo | - | yes | ok |
| /api/sonography | src/VoiceStudio.App/ViewModels/SonographyVisualizationViewModel.cs:135, src/VoiceStudio.App/ViewModels/SonographyVisualizationViewModel.cs:166, src/VoiceStudio.App/ViewModels/SonographyVisualizationViewModel.cs:192 | yes | ok |
| /api/spatial-audio | src/VoiceStudio.App/ViewModels/SpatialAudioViewModel.cs:129, src/VoiceStudio.App/ViewModels/SpatialAudioViewModel.cs:165, src/VoiceStudio.App/ViewModels/SpatialAudioViewModel.cs:217, src/VoiceStudio.App/ViewModels/SpatialAudioViewModel.cs:253, src/VoiceStudio.App/ViewModels/SpatialStageViewModel.cs:122 (+5 more) | yes | ok |
| /api/spectral | - | yes | ok |
| /api/spectrogram | src/VoiceStudio.App/ViewModels/SpectrogramViewModel.cs:151, src/VoiceStudio.App/ViewModels/SpectrogramViewModel.cs:221, src/VoiceStudio.App/ViewModels/SpectrogramViewModel.cs:263, src/VoiceStudio.App/ViewModels/SpectrogramViewModel.cs:305 | yes | ok |
| /api/ssml | src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:232, src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:288, src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:358, src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:403, src/VoiceStudio.App/ViewModels/SSMLControlViewModel.cs:473 (+2 more) | yes | ok |
| /api/style-transfer | src/VoiceStudio.App/ViewModels/StyleTransferViewModel.cs:207, src/VoiceStudio.App/ViewModels/StyleTransferViewModel.cs:266, src/VoiceStudio.App/ViewModels/StyleTransferViewModel.cs:302, src/VoiceStudio.App/ViewModels/StyleTransferViewModel.cs:345, src/VoiceStudio.App/ViewModels/VoiceStyleTransferViewModel.cs:132 (+2 more) | yes | ok |
| /api/tags | src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:171, src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:232, src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:303, src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:345, src/VoiceStudio.App/ViewModels/TagManagerViewModel.cs:440 (+4 more) | yes | ok |
| /api/telemetry | - | yes | ok |
| /api/templates | src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:145, src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:219, src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:291, src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:330, src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs:394 (+1 more) | yes | ok |
| /api/text-highlighting | src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:180, src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:234, src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:296, src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:343, src/VoiceStudio.App/ViewModels/TextHighlightingViewModel.cs:408 (+1 more) | no | allowlisted (panel hidden) |
| /api/timeline | src/VoiceStudio.App/UseCases/TimelineUseCase.cs:26, src/VoiceStudio.App/UseCases/TimelineUseCase.cs:39, src/VoiceStudio.App/UseCases/TimelineUseCase.cs:46, src/VoiceStudio.App/UseCases/TimelineUseCase.cs:53, src/VoiceStudio.App/UseCases/TimelineUseCase.cs:60 (+10 more) | yes | ok |
| /api/todo-panel | src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:257, src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:341, src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:420, src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:480, src/VoiceStudio.App/ViewModels/TodoPanelViewModel.cs:514 (+2 more) | no | allowlisted (panel hidden) |
| /api/tracing | - | yes | ok |
| /api/training | src/VoiceStudio.App/Services/BackendClient.cs:2642, src/VoiceStudio.App/Services/BackendClient.cs:2658, src/VoiceStudio.App/Services/BackendClient.cs:2674, src/VoiceStudio.App/Services/BackendClient.cs:2690, src/VoiceStudio.App/Services/BackendClient.cs:2705 (+6 more) | yes | ok |
| /api/transcribe | src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:137, src/VoiceStudio.App/Services/BackendClient.cs:2519, src/VoiceStudio.App/Services/BackendClient.cs:2536, src/VoiceStudio.App/Services/BackendClient.cs:2552, src/VoiceStudio.App/Services/BackendClient.cs:2573 (+3 more) | yes | ok |
| /api/translation | - | yes | ok |
| /api/ultimate-dashboard | src/VoiceStudio.App/ViewModels/UltimateDashboardViewModel.cs:68 | no | allowlisted (panel hidden) |
| /api/upscaling | src/VoiceStudio.App/ViewModels/UpscalingViewModel.cs:188, src/VoiceStudio.App/ViewModels/UpscalingViewModel.cs:321, src/VoiceStudio.App/ViewModels/UpscalingViewModel.cs:344, src/VoiceStudio.App/ViewModels/UpscalingViewModel.cs:396 | yes | ok |
| /api/v1 | src/VoiceStudio.App/Views/Panels/DiagnosticsViewModel.cs:1168, src/VoiceStudio.App/Views/Panels/SLODashboardViewModel.cs:110 | no | allowlisted (no backend) |
| /api/v2 | - | yes | ok |
| /api/version | src/VoiceStudio.App/Services/BackendClient.cs:485, src/VoiceStudio.App/Services/BackendClient.cs:565 | yes | ok |
| /api/video | src/VoiceStudio.App/Services/BackendClient.cs:3272, src/VoiceStudio.App/Services/BackendClient.cs:3294, src/VoiceStudio.App/Services/BackendClient.cs:3313, src/VoiceStudio.App/Services/BackendClient.cs:3331, src/VoiceStudio.App/Services/BackendClient.cs:3369 (+3 more) | yes | ok |
| /api/visualization | src/VoiceStudio.App/Views/Panels/AdvancedRealTimeVisualizationViewModel.cs:294 | no | allowlisted (no backend) |
| /api/voice | src/VoiceStudio.App/Core/Engines/BackendEngineAdapter.cs:102, src/VoiceStudio.App/Services/BackendClient.cs:639, src/VoiceStudio.App/Services/BackendClient.cs:680, src/VoiceStudio.App/Services/BackendClient.cs:743, src/VoiceStudio.App/Services/BackendClient.cs:1017 (+23 more) | yes | ok |
| /api/voice-browser | src/VoiceStudio.App/ViewModels/VoiceBrowserViewModel.cs:137, src/VoiceStudio.App/ViewModels/VoiceBrowserViewModel.cs:178, src/VoiceStudio.App/ViewModels/VoiceBrowserViewModel.cs:208 | yes | ok |
| /api/voice-effects | - | yes | ok |
| /api/voice-morph | src/VoiceStudio.App/ViewModels/VoiceMorphingBlendingViewModel.cs:226, src/VoiceStudio.App/ViewModels/VoiceMorphingBlendingViewModel.cs:281, src/VoiceStudio.App/ViewModels/VoiceMorphingBlendingViewModel.cs:350, src/VoiceStudio.App/ViewModels/VoiceMorphViewModel.cs:165, src/VoiceStudio.App/ViewModels/VoiceMorphViewModel.cs:231 (+3 more) | yes | ok |
| /api/voice-speech | - | yes | ok |
| /api/waveform | src/VoiceStudio.App/ViewModels/AdvancedWaveformVisualizationViewModel.cs:202, src/VoiceStudio.App/ViewModels/AdvancedWaveformVisualizationViewModel.cs:257, src/VoiceStudio.App/ViewModels/AdvancedWaveformVisualizationViewModel.cs:297 | yes | ok |
| /api/workflows | src/VoiceStudio.App/Services/BackendClient.cs:1872, src/VoiceStudio.App/Services/BackendClient.cs:1890, src/VoiceStudio.App/Services/BackendClient.cs:1906, src/VoiceStudio.App/Services/BackendClient.cs:1922, src/VoiceStudio.App/Services/BackendClient.cs:1938 (+1 more) | yes | ok |

## Allowlist (archived + panel hidden)

- /api/mcp-dashboard
- /api/script-editor
- /api/text-highlighting
- /api/todo-panel
- /api/ultimate-dashboard
