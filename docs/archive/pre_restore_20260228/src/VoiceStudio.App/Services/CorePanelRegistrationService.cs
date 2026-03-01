using System;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service to register all core panels in the PanelRegistry.
  /// These are the main panels that were previously hardcoded in MainWindow.
  /// </summary>
  public static class CorePanelRegistrationService
  {
    /// <summary>
    /// Registers all core panels in the PanelRegistry.
    /// </summary>
    public static void RegisterCorePanels(IPanelRegistry registry)
    {
      if (registry == null)
        throw new ArgumentNullException(nameof(registry));

      // Core synthesis panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VoiceSynthesis",
        DisplayName = "Voice Synthesis",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceSynthesisView),
        ViewModelType = typeof(VoiceSynthesisViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EnsembleSynthesis",
        DisplayName = "Ensemble Synthesis",
        Region = PanelRegion.Center,
        ViewType = typeof(EnsembleSynthesisView),
        ViewModelType = typeof(EnsembleSynthesisViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "BatchProcessing",
        DisplayName = "Batch Processing",
        Region = PanelRegion.Center,
        ViewType = typeof(BatchProcessingView),
        ViewModelType = typeof(BatchProcessingViewModel)
      });

      // Training panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TrainingDatasetEditor",
        DisplayName = "Training Dataset Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(TrainingDatasetEditorView),
        ViewModelType = typeof(TrainingDatasetEditorViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ModelManager",
        DisplayName = "Model Manager",
        Region = PanelRegion.Center,
        ViewType = typeof(ModelManagerView),
        ViewModelType = typeof(ModelManagerViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Training",
        DisplayName = "Training",
        Region = PanelRegion.Left,
        ViewType = typeof(TrainingView),
        ViewModelType = typeof(TrainingViewModel)
      });

      // Audio processing panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Transcribe",
        DisplayName = "Transcribe",
        Region = PanelRegion.Center,
        ViewType = typeof(TranscribeView),
        ViewModelType = typeof(TranscribeViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Recording",
        DisplayName = "Recording",
        Region = PanelRegion.Center,
        ViewType = typeof(RecordingView),
        ViewModelType = typeof(RecordingViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AudioAnalysis",
        DisplayName = "Audio Analysis",
        Region = PanelRegion.Center,
        ViewType = typeof(AudioAnalysisView),
        ViewModelType = typeof(AudioAnalysisViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "QualityControl",
        DisplayName = "Quality Control",
        Region = PanelRegion.Right,
        ViewType = typeof(QualityControlView),
        ViewModelType = typeof(QualityControlViewModel)
      });

      // Navigation panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Timeline",
        DisplayName = "Timeline",
        Region = PanelRegion.Center,
        ViewType = typeof(TimelineView),
        ViewModelType = typeof(TimelineViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Profiles",
        DisplayName = "Profiles",
        Region = PanelRegion.Left,
        ViewType = typeof(ProfilesView),
        ViewModelType = typeof(ProfilesViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Library",
        DisplayName = "Library",
        Region = PanelRegion.Left,
        ViewType = typeof(LibraryView),
        ViewModelType = typeof(LibraryViewModel)
      });

      // Effect panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EffectsMixer",
        DisplayName = "Effects Mixer",
        Region = PanelRegion.Right,
        ViewType = typeof(EffectsMixerView),
        ViewModelType = typeof(EffectsMixerViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Analyzer",
        DisplayName = "Analyzer",
        Region = PanelRegion.Right,
        ViewType = typeof(AnalyzerView),
        ViewModelType = typeof(AnalyzerViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VoiceMorph",
        DisplayName = "Voice Morph",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceMorphView),
        ViewModelType = typeof(VoiceMorphViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EmotionControl",
        DisplayName = "Emotion Control",
        Region = PanelRegion.Right,
        ViewType = typeof(EmotionControlView),
        ViewModelType = typeof(EmotionControlViewModel)
      });

      // Utility panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Diagnostics",
        DisplayName = "Diagnostics",
        Region = PanelRegion.Bottom,
        ViewType = typeof(DiagnosticsView),
        ViewModelType = typeof(DiagnosticsViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Settings",
        DisplayName = "Settings",
        Region = PanelRegion.Right,
        ViewType = typeof(SettingsView),
        ViewModelType = typeof(SettingsViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Help",
        DisplayName = "Help",
        Region = PanelRegion.Right,
        ViewType = typeof(HelpView),
        ViewModelType = typeof(HelpViewModel)
      });

      // Advanced panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "SSMLControl",
        DisplayName = "SSML Control",
        Region = PanelRegion.Right,
        ViewType = typeof(SSMLControlView),
        ViewModelType = typeof(SSMLControlViewModel)
      });

      // Voice cloning panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VoiceQuickClone",
        DisplayName = "Quick Clone",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceQuickCloneView),
        ViewModelType = typeof(VoiceQuickCloneViewModel)
      });

      // Quality panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "QualityDashboard",
        DisplayName = "Quality Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(QualityDashboardView),
        ViewModelType = typeof(QualityDashboardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "QualityBenchmark",
        DisplayName = "Quality Benchmark",
        Region = PanelRegion.Center,
        ViewType = typeof(QualityBenchmarkView),
        ViewModelType = typeof(QualityBenchmarkViewModel)
      });

      // Image/Video panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ImageGen",
        DisplayName = "Image Generation",
        Region = PanelRegion.Center,
        ViewType = typeof(ImageGenView),
        ViewModelType = typeof(ImageGenViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VideoGen",
        DisplayName = "Video Generation",
        Region = PanelRegion.Center,
        ViewType = typeof(VideoGenView),
        ViewModelType = typeof(VideoGenViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "DeepfakeCreator",
        DisplayName = "Deepfake Creator",
        Region = PanelRegion.Center,
        ViewType = typeof(DeepfakeCreatorView),
        ViewModelType = typeof(DeepfakeCreatorViewModel)
      });

      // Script/Scene panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "DatasetQA",
        DisplayName = "Dataset QA",
        Region = PanelRegion.Center,
        ViewType = typeof(DatasetQAView),
        ViewModelType = typeof(DatasetQAViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ScriptEditor",
        DisplayName = "Script Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(ScriptEditorView),
        ViewModelType = typeof(ScriptEditorViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "SceneBuilder",
        DisplayName = "Scene Builder",
        Region = PanelRegion.Center,
        ViewType = typeof(SceneBuilderView),
        ViewModelType = typeof(SceneBuilderViewModel)
      });

      // Automation panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Macro",
        DisplayName = "Macro",
        Region = PanelRegion.Center,
        ViewType = typeof(MacroView),
        ViewModelType = typeof(MacroViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "WorkflowAutomation",
        DisplayName = "Workflow Automation",
        Region = PanelRegion.Center,
        ViewType = typeof(WorkflowAutomationView),
        ViewModelType = typeof(WorkflowAutomationViewModel)
      });

      // Settings panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AdvancedSettings",
        DisplayName = "Advanced Settings",
        Region = PanelRegion.Right,
        ViewType = typeof(AdvancedSettingsView),
        ViewModelType = typeof(AdvancedSettingsViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "APIKeyManager",
        DisplayName = "API Key Manager",
        Region = PanelRegion.Right,
        ViewType = typeof(APIKeyManagerView),
        ViewModelType = typeof(APIKeyManagerViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "GPUStatus",
        DisplayName = "GPU Status",
        Region = PanelRegion.Right,
        ViewType = typeof(GPUStatusView),
        ViewModelType = typeof(GPUStatusViewModel)
      });

      // Todo panel
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TodoPanel",
        DisplayName = "Todo Panel",
        Region = PanelRegion.Right,
        ViewType = typeof(TodoPanelView),
        ViewModelType = typeof(TodoPanelViewModel)
      });

      // Legacy panel migration batch: register panels that were previously
      // only reachable through MainWindow fallback logic.
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "SpatialStage",
        DisplayName = "Spatial Stage",
        Region = PanelRegion.Center,
        ViewType = typeof(SpatialStageView),
        ViewModelType = typeof(SpatialStageViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "HealthCheck",
        DisplayName = "Health Check",
        Region = PanelRegion.Right,
        ViewType = typeof(HealthCheckView)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MiniTimeline",
        DisplayName = "Mini Timeline",
        Region = PanelRegion.Bottom,
        ViewType = typeof(MiniTimelineView),
        ViewModelType = typeof(MiniTimelineViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "JobProgress",
        DisplayName = "Job Progress",
        Region = PanelRegion.Bottom,
        ViewType = typeof(JobProgressView),
        ViewModelType = typeof(JobProgressViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MCPDashboard",
        DisplayName = "MCP Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(MCPDashboardView),
        ViewModelType = typeof(MCPDashboardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "SLODashboard",
        DisplayName = "SLO Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(SLODashboardView),
        ViewModelType = typeof(SLODashboardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AudioMonitoringDashboard",
        DisplayName = "Audio Monitoring Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(AudioMonitoringDashboardView),
        ViewModelType = typeof(AudioMonitoringDashboardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AnalyticsDashboard",
        DisplayName = "Analytics Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(AnalyticsDashboardView),
        ViewModelType = typeof(AnalyticsDashboardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "UltimateDashboard",
        DisplayName = "Ultimate Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(UltimateDashboardView),
        ViewModelType = typeof(UltimateDashboardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "BackupRestore",
        DisplayName = "Backup & Restore",
        Region = PanelRegion.Center,
        ViewType = typeof(BackupRestoreView),
        ViewModelType = typeof(BackupRestoreViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "PluginManagement",
        DisplayName = "Plugin Management",
        Region = PanelRegion.Center,
        ViewType = typeof(PluginManagementView),
        ViewModelType = typeof(PluginManagementViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "PluginDetail",
        DisplayName = "Plugin Detail",
        Region = PanelRegion.Center,
        ViewType = typeof(PluginDetailView)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "PluginHealthDashboard",
        DisplayName = "Plugin Health Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(PluginHealthDashboardView),
        ViewModelType = typeof(PluginHealthDashboardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ProfileComparison",
        DisplayName = "Profile Comparison",
        Region = PanelRegion.Center,
        ViewType = typeof(ProfileComparisonView),
        ViewModelType = typeof(ProfileComparisonViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ProfileHealthDashboard",
        DisplayName = "Profile Health Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(ProfileHealthDashboardView),
        ViewModelType = typeof(ProfileHealthDashboardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ABTesting",
        DisplayName = "A/B Testing",
        Region = PanelRegion.Center,
        ViewType = typeof(ABTestingView),
        ViewModelType = typeof(ABTestingViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Spectrogram",
        DisplayName = "Spectrogram",
        Region = PanelRegion.Center,
        ViewType = typeof(SpectrogramView),
        ViewModelType = typeof(SpectrogramViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "RealTimeAudioVisualizer",
        DisplayName = "Real-Time Audio Visualizer",
        Region = PanelRegion.Center,
        ViewType = typeof(RealTimeAudioVisualizerView),
        ViewModelType = typeof(RealTimeAudioVisualizerViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "SonographyVisualization",
        DisplayName = "Sonography Visualization",
        Region = PanelRegion.Center,
        ViewType = typeof(SonographyVisualizationView),
        ViewModelType = typeof(SonographyVisualizationViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AdvancedRealTimeVisualization",
        DisplayName = "Advanced Real-Time Visualization",
        Region = PanelRegion.Center,
        ViewType = typeof(AdvancedRealTimeVisualizationView),
        ViewModelType = typeof(AdvancedRealTimeVisualizationViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Automation",
        DisplayName = "Automation",
        Region = PanelRegion.Center,
        ViewType = typeof(AutomationView),
        ViewModelType = typeof(AutomationViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "PipelineConversation",
        DisplayName = "Pipeline Conversation",
        Region = PanelRegion.Center,
        ViewType = typeof(PipelineConversationView),
        ViewModelType = typeof(PipelineConversationViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "PresetLibrary",
        DisplayName = "Preset Library",
        Region = PanelRegion.Left,
        ViewType = typeof(PresetLibraryView),
        ViewModelType = typeof(PresetLibraryViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TemplateLibrary",
        DisplayName = "Template Library",
        Region = PanelRegion.Left,
        ViewType = typeof(TemplateLibraryView),
        ViewModelType = typeof(TemplateLibraryViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TagManager",
        DisplayName = "Tag Manager",
        Region = PanelRegion.Right,
        ViewType = typeof(TagManagerView),
        ViewModelType = typeof(TagManagerViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TagOrganization",
        DisplayName = "Tag Organization",
        Region = PanelRegion.Right,
        ViewType = typeof(TagOrganizationView),
        ViewModelType = typeof(TagOrganizationViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VoiceBrowser",
        DisplayName = "Voice Browser",
        Region = PanelRegion.Left,
        ViewType = typeof(VoiceBrowserView),
        ViewModelType = typeof(VoiceBrowserViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MarkerManager",
        DisplayName = "Marker Manager",
        Region = PanelRegion.Right,
        ViewType = typeof(MarkerManagerView),
        ViewModelType = typeof(MarkerManagerViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MixAssistant",
        DisplayName = "Mix Assistant",
        Region = PanelRegion.Right,
        ViewType = typeof(MixAssistantView),
        ViewModelType = typeof(MixAssistantViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ImageSearch",
        DisplayName = "Image Search",
        Region = PanelRegion.Left,
        ViewType = typeof(ImageSearchView),
        ViewModelType = typeof(ImageSearchViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Upscaling",
        DisplayName = "Upscaling",
        Region = PanelRegion.Center,
        ViewType = typeof(UpscalingView),
        ViewModelType = typeof(UpscalingViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VideoEdit",
        DisplayName = "Video Edit",
        Region = PanelRegion.Center,
        ViewType = typeof(VideoEditView),
        ViewModelType = typeof(VideoEditViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ImageVideoEnhancementPipeline",
        DisplayName = "Image/Video Enhancement Pipeline",
        Region = PanelRegion.Center,
        ViewType = typeof(ImageVideoEnhancementPipelineView),
        ViewModelType = typeof(ImageVideoEnhancementPipelineViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MultiVoiceGenerator",
        DisplayName = "Multi-Voice Generator",
        Region = PanelRegion.Center,
        ViewType = typeof(MultiVoiceGeneratorView),
        ViewModelType = typeof(MultiVoiceGeneratorViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "RealTimeVoiceConverter",
        DisplayName = "Real-Time Voice Converter",
        Region = PanelRegion.Center,
        ViewType = typeof(RealTimeVoiceConverterView),
        ViewModelType = typeof(RealTimeVoiceConverterViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TextHighlighting",
        DisplayName = "Text Highlighting",
        Region = PanelRegion.Center,
        ViewType = typeof(TextHighlightingView),
        ViewModelType = typeof(TextHighlightingViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TextBasedSpeechEditor",
        DisplayName = "Text-Based Speech Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(TextBasedSpeechEditorView),
        ViewModelType = typeof(TextBasedSpeechEditorViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EmotionStyleControl",
        DisplayName = "Emotion Style Control",
        Region = PanelRegion.Right,
        ViewType = typeof(EmotionStyleControlView),
        ViewModelType = typeof(EmotionStyleControlViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EmotionStylePresetEditor",
        DisplayName = "Emotion Style Preset Editor",
        Region = PanelRegion.Right,
        ViewType = typeof(EmotionStylePresetEditorView),
        ViewModelType = typeof(EmotionStylePresetEditorViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MultilingualSupport",
        DisplayName = "Multilingual Support",
        Region = PanelRegion.Right,
        ViewType = typeof(MultilingualSupportView),
        ViewModelType = typeof(MultilingualSupportViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AssistantView",
        DisplayName = "Assistant",
        Region = PanelRegion.Right,
        ViewType = typeof(AssistantView),
        ViewModelType = typeof(AssistantViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AdvancedSearch",
        DisplayName = "Advanced Search",
        Region = PanelRegion.Center,
        ViewType = typeof(AdvancedSearchView),
        ViewModelType = typeof(AdvancedSearchViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EngineParameterTuning",
        DisplayName = "Engine Parameter Tuning",
        Region = PanelRegion.Right,
        ViewType = typeof(EngineParameterTuningView),
        ViewModelType = typeof(EngineParameterTuningViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EngineRecommendation",
        DisplayName = "Engine Recommendation",
        Region = PanelRegion.Right,
        ViewType = typeof(EngineRecommendationView),
        ViewModelType = typeof(EngineRecommendationViewModel)
      });

      // EngineSetupWizard panel: available via manual navigation, not auto-registered
      // to avoid XAML compilation side effects during startup.
      // Use: NavigationBridge.NavigateToPanel("EngineSetupWizard")

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TrainingQualityVisualization",
        DisplayName = "Training Quality Visualization",
        Region = PanelRegion.Center,
        ViewType = typeof(TrainingQualityVisualizationView),
        ViewModelType = typeof(TrainingQualityVisualizationViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "QualityOptimizationWizard",
        DisplayName = "Quality Optimization Wizard",
        Region = PanelRegion.Center,
        ViewType = typeof(QualityOptimizationWizardView),
        ViewModelType = typeof(QualityOptimizationWizardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "LexiconView",
        DisplayName = "Lexicon",
        Region = PanelRegion.Right,
        ViewType = typeof(LexiconView),
        ViewModelType = typeof(LexiconViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VoiceCloningWizard",
        DisplayName = "Voice Cloning Wizard",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceCloningWizardView),
        ViewModelType = typeof(VoiceCloningWizardViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "StyleTransfer",
        DisplayName = "Style Transfer",
        Region = PanelRegion.Center,
        ViewType = typeof(StyleTransferView),
        ViewModelType = typeof(StyleTransferViewModel)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AdvancedSpectrogramVisualization",
        DisplayName = "Advanced Spectrogram Visualization",
        Region = PanelRegion.Center,
        ViewType = typeof(AdvancedSpectrogramVisualizationView)
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AdvancedWaveformVisualization",
        DisplayName = "Advanced Waveform Visualization",
        Region = PanelRegion.Center,
        ViewType = typeof(AdvancedWaveformVisualizationView)
      });
    }

    /// <summary>
    /// Registers a panel if it's not already registered (avoids conflicts
    /// with AdvancedPanelRegistrationService).
    /// </summary>
    private static void RegisterIfNotExists(IPanelRegistry registry, PanelDescriptor descriptor)
    {
      if (!registry.IsRegistered(descriptor.PanelId))
      {
        registry.Register(descriptor);
      }
    }
  }
}
