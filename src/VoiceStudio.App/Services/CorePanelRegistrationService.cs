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
        PanelId = PanelIds.VoiceSynthesis,
        DisplayName = "Voice Synthesis",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceSynthesisView),
        ViewModelType = typeof(VoiceSynthesisViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "voice", "synthesis", "TTS", "text-to-speech", "speak" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.SpeechToSpeech,
        DisplayName = "Speech to Speech",
        Region = PanelRegion.Center,
        ViewType = typeof(SpeechToSpeechView),
        ViewModelType = typeof(SpeechToSpeechViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "speech", "STS", "RVC", "voice conversion", "speech-to-speech" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.EnsembleSynthesis,
        DisplayName = "Ensemble Synthesis",
        Region = PanelRegion.Center,
        ViewType = typeof(EnsembleSynthesisView),
        ViewModelType = typeof(EnsembleSynthesisViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "ensemble", "multi-voice", "synthesis", "blend" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.BatchProcessing,
        DisplayName = "Batch Processing",
        Region = PanelRegion.Center,
        ViewType = typeof(BatchProcessingView),
        ViewModelType = typeof(BatchProcessingViewModel),
        MenuCategory = "Automation",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "batch", "bulk", "queue", "automation", "jobs" }
      });

      // Training panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.TrainingDatasetEditor,
        DisplayName = "Training Dataset Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(TrainingDatasetEditorView),
        ViewModelType = typeof(TrainingDatasetEditorViewModel),
        MenuCategory = "Training",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "dataset", "training", "samples", "edit", "voice clone" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.ModelManager,
        DisplayName = "Model Manager",
        Region = PanelRegion.Center,
        ViewType = typeof(ModelManagerView),
        ViewModelType = typeof(ModelManagerViewModel),
        MenuCategory = "Training",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "model", "checkpoint", "training", "manage", "export" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Training,
        DisplayName = "Training",
        Region = PanelRegion.Left,
        ViewType = typeof(TrainingView),
        ViewModelType = typeof(TrainingViewModel),
        MenuCategory = "Training",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "training", "train", "fine-tune", "voice model" }
      });

      // Audio processing panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Transcribe,
        DisplayName = "Transcribe",
        Region = PanelRegion.Center,
        ViewType = typeof(TranscribeView),
        ViewModelType = typeof(TranscribeViewModel),
        MenuCategory = "Audio",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "transcribe", "speech-to-text", "STT", "transcription" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Recording,
        DisplayName = "Recording",
        Region = PanelRegion.Right,
        ViewType = typeof(RecordingView),
        ViewModelType = typeof(RecordingViewModel),
        MenuCategory = "Audio",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "record", "microphone", "audio", "capture" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.AudioAnalysis,
        DisplayName = "Audio Analysis",
        Region = PanelRegion.Center,
        ViewType = typeof(AudioAnalysisView),
        ViewModelType = typeof(AudioAnalysisViewModel),
        MenuCategory = "Audio",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "audio", "analysis", "waveform", "spectrum" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.QualityControl,
        DisplayName = "Quality Control",
        Region = PanelRegion.Right,
        ViewType = typeof(QualityControlView),
        ViewModelType = typeof(QualityControlViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "quality", "QC", "MOS", "assessment" }
      });

      // Navigation panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Timeline,
        DisplayName = "Timeline",
        Region = PanelRegion.Center,
        ViewType = typeof(TimelineView),
        ViewModelType = typeof(TimelineViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "timeline", "tracks", "edit", "arrange", "project" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Profiles,
        DisplayName = "Profiles",
        Region = PanelRegion.Left,
        ViewType = typeof(ProfilesView),
        ViewModelType = typeof(ProfilesViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "profiles", "voices", "presets", "manage" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Library,
        DisplayName = "Library",
        Region = PanelRegion.Left,
        ViewType = typeof(LibraryView),
        ViewModelType = typeof(LibraryViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "library", "assets", "media", "files", "browse" }
      });

      // Effect panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.EffectsMixer,
        DisplayName = "Effects Mixer",
        Region = PanelRegion.Right,
        ViewType = typeof(EffectsMixerView),
        ViewModelType = typeof(EffectsMixerViewModel),
        MenuCategory = "Audio",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "effects", "mixer", "EQ", "audio", "process" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Analyzer,
        DisplayName = "Analyzer",
        Region = PanelRegion.Right,
        ViewType = typeof(AnalyzerView),
        ViewModelType = typeof(AnalyzerViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "analyzer", "spectrum", "frequency", "audio" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.VoiceMorph,
        DisplayName = "Voice Morph",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceMorphView),
        ViewModelType = typeof(VoiceMorphViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Experimental,
        Keywords = new[] { "morph", "voice", "blend", "interpolate" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.EmotionControl,
        DisplayName = "Emotion Control",
        Region = PanelRegion.Right,
        ViewType = typeof(EmotionControlView),
        ViewModelType = typeof(EmotionControlViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "emotion", "prosody", "expression", "voice" }
      });

      // Utility panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Diagnostics,
        DisplayName = "Diagnostics",
        Region = PanelRegion.Bottom,
        ViewType = typeof(DiagnosticsView),
        ViewModelType = typeof(DiagnosticsViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "diagnostics", "logs", "debug", "system" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Settings,
        DisplayName = "Settings",
        Region = PanelRegion.Right,
        ViewType = typeof(SettingsView),
        ViewModelType = typeof(SettingsViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "settings", "preferences", "config", "options" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Help,
        DisplayName = "Help",
        Region = PanelRegion.Right,
        ViewType = typeof(HelpView),
        ViewModelType = typeof(HelpViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "help", "docs", "about", "support" }
      });

      // Advanced panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.SSMLControl,
        DisplayName = "SSML Control",
        Region = PanelRegion.Right,
        ViewType = typeof(SSMLControlView),
        ViewModelType = typeof(SSMLControlViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "SSML", "markup", "speech", "tags" }
      });

      // Voice cloning panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.VoiceQuickClone,
        DisplayName = "Quick Clone",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceQuickCloneView),
        ViewModelType = typeof(VoiceQuickCloneViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "clone", "voice", "quick", "sample" }
      });

      // Quality panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.QualityDashboard,
        DisplayName = "Quality Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(QualityDashboardView),
        ViewModelType = typeof(QualityDashboardViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "quality", "dashboard", "metrics", "MOS" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.QualityBenchmark,
        DisplayName = "Quality Benchmark",
        Region = PanelRegion.Center,
        ViewType = typeof(QualityBenchmarkView),
        ViewModelType = typeof(QualityBenchmarkViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "benchmark", "quality", "compare", "test" }
      });

      // Image/Video panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.ImageGen,
        DisplayName = "Image Generation",
        Region = PanelRegion.Center,
        ViewType = typeof(ImageGenView),
        ViewModelType = typeof(ImageGenViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Experimental,
        Keywords = new[] { "image", "generate", "AI", "picture" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.VideoGen,
        DisplayName = "Video Generation",
        Region = PanelRegion.Center,
        ViewType = typeof(VideoGenView),
        ViewModelType = typeof(VideoGenViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Experimental,
        Keywords = new[] { "video", "generate", "AI", "clip" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.DeepfakeCreator,
        DisplayName = "Deepfake Creator",
        Region = PanelRegion.Center,
        ViewType = typeof(DeepfakeCreatorView),
        ViewModelType = typeof(DeepfakeCreatorViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Experimental,
        Keywords = new[] { "deepfake", "face", "video", "lip-sync" }
      });

      // Script/Scene panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.DatasetQA,
        DisplayName = "Dataset QA",
        Region = PanelRegion.Center,
        ViewType = typeof(DatasetQAView),
        ViewModelType = typeof(DatasetQAViewModel),
        MenuCategory = "Training",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "dataset", "QA", "quality", "review" }
      });

      // F-10: Restored - /api/script-editor unarchived with file-based persistence (Fix 2)
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.ScriptEditor,
        DisplayName = "Script Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(ScriptEditorView),
        ViewModelType = typeof(ScriptEditorViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "script", "editor", "text", "dialogue" },
        IsVisible = true
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.SceneBuilder,
        DisplayName = "Scene Builder",
        Region = PanelRegion.Center,
        ViewType = typeof(SceneBuilderView),
        ViewModelType = typeof(SceneBuilderViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "scene", "builder", "storyboard", "sequence" }
      });

      // Automation panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.Macro,
        DisplayName = "Macro",
        Region = PanelRegion.Bottom,
        ViewType = typeof(MacroView),
        ViewModelType = typeof(MacroViewModel),
        MenuCategory = "Automation",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "macro", "shortcut", "automation", "record" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.WorkflowAutomation,
        DisplayName = "Workflow Automation",
        Region = PanelRegion.Center,
        ViewType = typeof(WorkflowAutomationView),
        ViewModelType = typeof(WorkflowAutomationViewModel),
        MenuCategory = "Automation",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "workflow", "automation", "pipeline", "batch" }
      });

      // Settings panels
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.AdvancedSettings,
        DisplayName = "Advanced Settings",
        Region = PanelRegion.Right,
        ViewType = typeof(AdvancedSettingsView),
        ViewModelType = typeof(AdvancedSettingsViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "advanced", "settings", "config", "tweaks" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.APIKeyManager,
        DisplayName = "API Key Manager",
        Region = PanelRegion.Right,
        ViewType = typeof(APIKeyManagerView),
        ViewModelType = typeof(APIKeyManagerViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "API", "key", "credentials", "secrets" }
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.GPUStatus,
        DisplayName = "GPU Status",
        Region = PanelRegion.Right,
        ViewType = typeof(GPUStatusView),
        ViewModelType = typeof(GPUStatusViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "GPU", "CUDA", "hardware", "status" }
      });

      // F-10: Hidden - /api/todo-panel is archived
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.TodoPanel,
        DisplayName = "Todo Panel",
        Region = PanelRegion.Right,
        ViewType = typeof(TodoPanelView),
        ViewModelType = typeof(TodoPanelViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable,
        Keywords = new[] { "todo", "tasks", "notes", "checklist" },
        IsVisible = false
      });

      // F-10: Hidden - /api/text-highlighting is archived
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.TextHighlighting,
        DisplayName = "Text Highlighting",
        Region = PanelRegion.Center,
        ViewType = typeof(TextHighlightingView),
        ViewModelType = typeof(TextHighlightingViewModel),
        MenuCategory = "Editing",
        Maturity = PanelMaturity.Beta,
        Keywords = new[] { "text", "highlight", "audio", "sync" },
        IsVisible = false
      });

      // F-10: Hidden - /api/ultimate-dashboard is archived
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = PanelIds.UltimateDashboard,
        DisplayName = "Ultimate Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(UltimateDashboardView),
        ViewModelType = typeof(UltimateDashboardViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Experimental,
        Keywords = new[] { "dashboard", "overview", "summary" },
        IsVisible = false
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
