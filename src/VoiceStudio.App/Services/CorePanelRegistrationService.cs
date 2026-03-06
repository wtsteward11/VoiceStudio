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
        ViewModelType = typeof(VoiceSynthesisViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EnsembleSynthesis",
        DisplayName = "Ensemble Synthesis",
        Region = PanelRegion.Center,
        ViewType = typeof(EnsembleSynthesisView),
        ViewModelType = typeof(EnsembleSynthesisViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "BatchProcessing",
        DisplayName = "Batch Processing",
        Region = PanelRegion.Center,
        ViewType = typeof(BatchProcessingView),
        ViewModelType = typeof(BatchProcessingViewModel),
        MenuCategory = "Automation",
        Maturity = PanelMaturity.Stable
      });

      // Training panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TrainingDatasetEditor",
        DisplayName = "Training Dataset Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(TrainingDatasetEditorView),
        ViewModelType = typeof(TrainingDatasetEditorViewModel),
        MenuCategory = "Training",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ModelManager",
        DisplayName = "Model Manager",
        Region = PanelRegion.Center,
        ViewType = typeof(ModelManagerView),
        ViewModelType = typeof(ModelManagerViewModel),
        MenuCategory = "Training",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Training",
        DisplayName = "Training",
        Region = PanelRegion.Left,
        ViewType = typeof(TrainingView),
        ViewModelType = typeof(TrainingViewModel),
        MenuCategory = "Training",
        Maturity = PanelMaturity.Stable
      });

      // Audio processing panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Transcribe",
        DisplayName = "Transcribe",
        Region = PanelRegion.Center,
        ViewType = typeof(TranscribeView),
        ViewModelType = typeof(TranscribeViewModel),
        MenuCategory = "Audio",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Recording",
        DisplayName = "Recording",
        Region = PanelRegion.Right,
        ViewType = typeof(RecordingView),
        ViewModelType = typeof(RecordingViewModel),
        MenuCategory = "Audio",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AudioAnalysis",
        DisplayName = "Audio Analysis",
        Region = PanelRegion.Center,
        ViewType = typeof(AudioAnalysisView),
        ViewModelType = typeof(AudioAnalysisViewModel),
        MenuCategory = "Audio",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "QualityControl",
        DisplayName = "Quality Control",
        Region = PanelRegion.Right,
        ViewType = typeof(QualityControlView),
        ViewModelType = typeof(QualityControlViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Stable
      });

      // Navigation panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Timeline",
        DisplayName = "Timeline",
        Region = PanelRegion.Center,
        ViewType = typeof(TimelineView),
        ViewModelType = typeof(TimelineViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Profiles",
        DisplayName = "Profiles",
        Region = PanelRegion.Left,
        ViewType = typeof(ProfilesView),
        ViewModelType = typeof(ProfilesViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Library",
        DisplayName = "Library",
        Region = PanelRegion.Left,
        ViewType = typeof(LibraryView),
        ViewModelType = typeof(LibraryViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable
      });

      // Effect panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EffectsMixer",
        DisplayName = "Effects Mixer",
        Region = PanelRegion.Right,
        ViewType = typeof(EffectsMixerView),
        ViewModelType = typeof(EffectsMixerViewModel),
        MenuCategory = "Audio",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Analyzer",
        DisplayName = "Analyzer",
        Region = PanelRegion.Right,
        ViewType = typeof(AnalyzerView),
        ViewModelType = typeof(AnalyzerViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VoiceMorph",
        DisplayName = "Voice Morph",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceMorphView),
        ViewModelType = typeof(VoiceMorphViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Experimental
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EmotionControl",
        DisplayName = "Emotion Control",
        Region = PanelRegion.Right,
        ViewType = typeof(EmotionControlView),
        ViewModelType = typeof(EmotionControlViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Beta
      });

      // Utility panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Diagnostics",
        DisplayName = "Diagnostics",
        Region = PanelRegion.Bottom,
        ViewType = typeof(DiagnosticsView),
        ViewModelType = typeof(DiagnosticsViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Settings",
        DisplayName = "Settings",
        Region = PanelRegion.Right,
        ViewType = typeof(SettingsView),
        ViewModelType = typeof(SettingsViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Help",
        DisplayName = "Help",
        Region = PanelRegion.Right,
        ViewType = typeof(HelpView),
        ViewModelType = typeof(HelpViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable
      });

      // Advanced panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "SSMLControl",
        DisplayName = "SSML Control",
        Region = PanelRegion.Right,
        ViewType = typeof(SSMLControlView),
        ViewModelType = typeof(SSMLControlViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Beta
      });

      // Voice cloning panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VoiceQuickClone",
        DisplayName = "Quick Clone",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceQuickCloneView),
        ViewModelType = typeof(VoiceQuickCloneViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Beta
      });

      // Quality panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "QualityDashboard",
        DisplayName = "Quality Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(QualityDashboardView),
        ViewModelType = typeof(QualityDashboardViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "QualityBenchmark",
        DisplayName = "Quality Benchmark",
        Region = PanelRegion.Center,
        ViewType = typeof(QualityBenchmarkView),
        ViewModelType = typeof(QualityBenchmarkViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta
      });

      // Image/Video panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ImageGen",
        DisplayName = "Image Generation",
        Region = PanelRegion.Center,
        ViewType = typeof(ImageGenView),
        ViewModelType = typeof(ImageGenViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Experimental
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VideoGen",
        DisplayName = "Video Generation",
        Region = PanelRegion.Center,
        ViewType = typeof(VideoGenView),
        ViewModelType = typeof(VideoGenViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Experimental
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "DeepfakeCreator",
        DisplayName = "Deepfake Creator",
        Region = PanelRegion.Center,
        ViewType = typeof(DeepfakeCreatorView),
        ViewModelType = typeof(DeepfakeCreatorViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Experimental
      });

      // Script/Scene panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "DatasetQA",
        DisplayName = "Dataset QA",
        Region = PanelRegion.Center,
        ViewType = typeof(DatasetQAView),
        ViewModelType = typeof(DatasetQAViewModel),
        MenuCategory = "Training",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ScriptEditor",
        DisplayName = "Script Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(ScriptEditorView),
        ViewModelType = typeof(ScriptEditorViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "SceneBuilder",
        DisplayName = "Scene Builder",
        Region = PanelRegion.Center,
        ViewType = typeof(SceneBuilderView),
        ViewModelType = typeof(SceneBuilderViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Beta
      });

      // Automation panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Macro",
        DisplayName = "Macro",
        Region = PanelRegion.Bottom,
        ViewType = typeof(MacroView),
        ViewModelType = typeof(MacroViewModel),
        MenuCategory = "Automation",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "WorkflowAutomation",
        DisplayName = "Workflow Automation",
        Region = PanelRegion.Center,
        ViewType = typeof(WorkflowAutomationView),
        ViewModelType = typeof(WorkflowAutomationViewModel),
        MenuCategory = "Automation",
        Maturity = PanelMaturity.Stable
      });

      // Settings panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "AdvancedSettings",
        DisplayName = "Advanced Settings",
        Region = PanelRegion.Right,
        ViewType = typeof(AdvancedSettingsView),
        ViewModelType = typeof(AdvancedSettingsViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "APIKeyManager",
        DisplayName = "API Key Manager",
        Region = PanelRegion.Right,
        ViewType = typeof(APIKeyManagerView),
        ViewModelType = typeof(APIKeyManagerViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "GPUStatus",
        DisplayName = "GPU Status",
        Region = PanelRegion.Right,
        ViewType = typeof(GPUStatusView),
        ViewModelType = typeof(GPUStatusViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Beta
      });

      // Todo panel
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TodoPanel",
        DisplayName = "Todo Panel",
        Region = PanelRegion.Right,
        ViewType = typeof(TodoPanelView),
        ViewModelType = typeof(TodoPanelViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable
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
