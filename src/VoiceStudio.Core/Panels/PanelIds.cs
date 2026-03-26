using System.Collections.Generic;

namespace VoiceStudio.Core.Panels
{
  /// <summary>
  /// Canonical panel IDs. Registry and ViewModels MUST use these constants.
  /// Convention: PascalCase. Single source of truth for workspace persistence and restore.
  /// </summary>
  public static class PanelIds
  {
    // Core synthesis
    public const string VoiceSynthesis = "VoiceSynthesis";
    public const string EnsembleSynthesis = "EnsembleSynthesis";
    public const string BatchProcessing = "BatchProcessing";

    // Training
    public const string TrainingDatasetEditor = "TrainingDatasetEditor";
    public const string ModelManager = "ModelManager";
    public const string Training = "Training";

    // Audio
    public const string Transcribe = "Transcribe";
    public const string Recording = "Recording";
    public const string AudioAnalysis = "AudioAnalysis";
    public const string QualityControl = "QualityControl";

    // Navigation / core
    public const string Timeline = "Timeline";
    public const string Profiles = "Profiles";
    public const string Library = "Library";
    public const string EffectsMixer = "EffectsMixer";
    public const string Analyzer = "Analyzer";

    // Voice
    public const string VoiceMorph = "VoiceMorph";
    public const string EmotionControl = "EmotionControl";

    // Utility
    public const string Diagnostics = "Diagnostics";
    public const string Settings = "Settings";
    public const string Help = "Help";

    // Advanced
    public const string SSMLControl = "SSMLControl";
    public const string VoiceQuickClone = "VoiceQuickClone";
    public const string QualityDashboard = "QualityDashboard";
    public const string QualityBenchmark = "QualityBenchmark";

    // Media
    public const string ImageGen = "ImageGen";
    public const string VideoGen = "VideoGen";
    public const string DeepfakeCreator = "DeepfakeCreator";

    // Script / scene
    public const string DatasetQA = "DatasetQA";
    public const string ScriptEditor = "ScriptEditor";
    public const string SceneBuilder = "SceneBuilder";

    // Automation
    public const string Macro = "Macro";
    public const string WorkflowAutomation = "WorkflowAutomation";

    // Settings
    public const string AdvancedSettings = "AdvancedSettings";
    public const string APIKeyManager = "APIKeyManager";
    public const string GPUStatus = "GPUStatus";
    public const string TodoPanel = "TodoPanel";
    public const string TextHighlighting = "TextHighlighting";
    public const string UltimateDashboard = "UltimateDashboard";

    // Advanced panels
    public const string TextSpeechEditor = "TextSpeechEditor";
    public const string Prosody = "Prosody";
    public const string SpatialAudio = "SpatialAudio";
    public const string AIMixingMastering = "AIMixingMastering";
    public const string VoiceStyleTransfer = "VoiceStyleTransfer";
    public const string EmbeddingExplorer = "EmbeddingExplorer";
    public const string AIProductionAssistant = "AIProductionAssistant";
    public const string PronunciationLexicon = "PronunciationLexicon";
    public const string VoiceMorphingBlending = "VoiceMorphingBlending";
    public const string PluginGallery = "PluginGallery";
    public const string ThemeEditor = "ThemeEditor";

    // Module panels
    public const string VoiceCloningWizard = "VoiceCloningWizard";
    public const string MultiVoiceGenerator = "MultiVoiceGenerator";
    public const string RealTimeConverter = "RealTimeConverter";
    public const string EmotionStyle = "EmotionStyle";
    public const string Multilingual = "Multilingual";
    public const string Spectrogram = "Spectrogram";
    public const string RealTimeVisualizer = "RealTimeVisualizer";
    public const string Sonography = "Sonography";
    public const string QualityOptimizer = "QualityOptimizer";
    public const string ABTesting = "ABTesting";
    public const string ProfileComparison = "ProfileComparison";
    public const string Upscaling = "Upscaling";
    public const string ImageSearch = "ImageSearch";
    public const string VideoEdit = "VideoEdit";
    public const string Automation = "Automation";
    public const string PresetLibrary = "PresetLibrary";
    public const string TemplateLibrary = "TemplateLibrary";
    public const string TagManager = "TagManager";
    public const string MarkerManager = "MarkerManager";
    public const string BackupRestore = "BackupRestore";
    public const string PluginManagement = "PluginManagement";
    public const string HealthCheck = "HealthCheck";
    public const string JobProgress = "JobProgress";
    public const string MCPDashboard = "MCPDashboard";

    /// <summary>
    /// All canonical panel IDs for validation and iteration.
    /// </summary>
    public static IReadOnlyList<string> All => new[]
    {
      VoiceSynthesis, EnsembleSynthesis, BatchProcessing,
      TrainingDatasetEditor, ModelManager, Training,
      Transcribe, Recording, AudioAnalysis, QualityControl,
      Timeline, Profiles, Library, EffectsMixer, Analyzer,
      VoiceMorph, EmotionControl,
      Diagnostics, Settings, Help,
      SSMLControl, VoiceQuickClone, QualityDashboard, QualityBenchmark,
      ImageGen, VideoGen, DeepfakeCreator,
      DatasetQA, ScriptEditor, SceneBuilder,
      Macro, WorkflowAutomation,
      AdvancedSettings, APIKeyManager, GPUStatus, TodoPanel, TextHighlighting, UltimateDashboard,
      TextSpeechEditor, Prosody, SpatialAudio, AIMixingMastering, VoiceStyleTransfer,
      EmbeddingExplorer, AIProductionAssistant, PronunciationLexicon, VoiceMorphingBlending,
      PluginGallery, ThemeEditor,
      VoiceCloningWizard, MultiVoiceGenerator, RealTimeConverter, EmotionStyle, Multilingual,
      Spectrogram, RealTimeVisualizer, Sonography, QualityOptimizer, ABTesting, ProfileComparison,
      Upscaling, ImageSearch, VideoEdit, Automation, PresetLibrary, TemplateLibrary,
      TagManager, MarkerManager, BackupRestore, PluginManagement, HealthCheck, JobProgress, MCPDashboard
    };
  }
}
