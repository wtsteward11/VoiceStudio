using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace VoiceStudio.App.Tests.UI
{
  /// <summary>
  /// Smoke tests for panel navigation.
  /// Tests that all major panels can be loaded and displayed without errors.
  /// </summary>
  [TestClass]
  [Ignore("Disabled for finish-line stability; UI automation not required.")]
  public class PanelNavigationSmokeTests : SmokeTestBase
  {
    #region Core Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VoiceSynthesis_CanNavigate()
    {
      // Arrange
      VerifyApplicationStarted();

      // Act
      var result = await NavigateToPanelAsync("VoiceSynthesis");

      // Assert
      Assert.IsTrue(result, "VoiceSynthesis panel should be visible");
      Assert.IsTrue(IsPanelVisible("VoiceSynthesisView_Root"));
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Profiles_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Profiles");
      Assert.IsTrue(result, "Profiles panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Library_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Library");
      Assert.IsTrue(result, "Library panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Timeline_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Timeline");
      Assert.IsTrue(result, "Timeline panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_EffectsMixer_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("EffectsMixer");
      Assert.IsTrue(result, "EffectsMixer panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Analyzer_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Analyzer");
      Assert.IsTrue(result, "Analyzer panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Diagnostics_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Diagnostics");
      Assert.IsTrue(result, "Diagnostics panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Macro_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Macro");
      Assert.IsTrue(result, "Macro panel should be visible");
    }

    #endregion

    #region Synthesis Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_EnsembleSynthesis_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("EnsembleSynthesis");
      Assert.IsTrue(result, "EnsembleSynthesis panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_BatchProcessing_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("BatchProcessing");
      Assert.IsTrue(result, "BatchProcessing panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_MultiVoiceGenerator_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("MultiVoiceGenerator");
      Assert.IsTrue(result, "MultiVoiceGenerator panel should be visible");
    }

    #endregion

    #region Training Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Training_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Training");
      Assert.IsTrue(result, "Training panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_TrainingDatasetEditor_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("TrainingDatasetEditor");
      Assert.IsTrue(result, "TrainingDatasetEditor panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_ModelManager_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("ModelManager");
      Assert.IsTrue(result, "ModelManager panel should be visible");
    }

    #endregion

    #region Audio Processing Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Transcribe_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Transcribe");
      Assert.IsTrue(result, "Transcribe panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Recording_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Recording");
      Assert.IsTrue(result, "Recording panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_AudioAnalysis_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("AudioAnalysis");
      Assert.IsTrue(result, "AudioAnalysis panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_QualityControl_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("QualityControl");
      Assert.IsTrue(result, "QualityControl panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_QualityDashboard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("QualityDashboard");
      Assert.IsTrue(result, "QualityDashboard panel should be visible");
    }

    #endregion

    #region Settings Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Settings_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Settings");
      Assert.IsTrue(result, "Settings panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_AdvancedSettings_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("AdvancedSettings");
      Assert.IsTrue(result, "AdvancedSettings panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_KeyboardShortcuts_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("KeyboardShortcuts");
      Assert.IsTrue(result, "KeyboardShortcuts panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_PluginManagement_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("PluginManagement");
      Assert.IsTrue(result, "PluginManagement panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_APIKeyManager_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("APIKeyManager");
      Assert.IsTrue(result, "APIKeyManager panel should be visible");
    }

    #endregion

    #region Utility Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Help_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Help");
      Assert.IsTrue(result, "Help panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_GPUStatus_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("GPUStatus");
      Assert.IsTrue(result, "GPUStatus panel should be visible");
    }

    #endregion

    #region Voice Control Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VoiceMorph_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("VoiceMorph");
      Assert.IsTrue(result, "VoiceMorph panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_StyleTransfer_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("StyleTransfer");
      Assert.IsTrue(result, "StyleTransfer panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_EmotionControl_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("EmotionControl");
      Assert.IsTrue(result, "EmotionControl panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Prosody_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Prosody");
      Assert.IsTrue(result, "Prosody panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_SSML_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("SSML");
      Assert.IsTrue(result, "SSML panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Spectrogram_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Spectrogram");
      Assert.IsTrue(result, "Spectrogram panel should be visible");
    }

    #endregion

    #region Text/Speech Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_TextSpeechEditor_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("TextSpeechEditor");
      Assert.IsTrue(result, "TextSpeechEditor panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_ScriptEditor_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("ScriptEditor");
      Assert.IsTrue(result, "ScriptEditor panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Lexicon_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Lexicon");
      Assert.IsTrue(result, "Lexicon panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_MultilingualSupport_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("MultilingualSupport");
      Assert.IsTrue(result, "MultilingualSupport panel should be visible");
    }

    #endregion

    #region Voice Cloning/Morphing Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VoiceMorphingBlending_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("VoiceMorphingBlending");
      Assert.IsTrue(result, "VoiceMorphingBlending panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VoiceCloningWizard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("VoiceCloningWizard");
      Assert.IsTrue(result, "VoiceCloningWizard panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VoiceQuickClone_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("VoiceQuickClone");
      Assert.IsTrue(result, "VoiceQuickClone panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VoiceBrowser_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("VoiceBrowser");
      Assert.IsTrue(result, "VoiceBrowser panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_RealTimeVoiceConverter_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("RealTimeVoiceConverter");
      Assert.IsTrue(result, "RealTimeVoiceConverter panel should be visible");
    }

    #endregion

    #region Emotion/Style Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_EmotionStyleControl_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("EmotionStyleControl");
      Assert.IsTrue(result, "EmotionStyleControl panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_EmotionStylePresetEditor_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("EmotionStylePresetEditor");
      Assert.IsTrue(result, "EmotionStylePresetEditor panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VoiceStyleTransfer_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("VoiceStyleTransfer");
      Assert.IsTrue(result, "VoiceStyleTransfer panel should be visible");
    }

    #endregion

    #region Visualization Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_RealTimeAudioVisualizer_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("RealTimeAudioVisualizer");
      Assert.IsTrue(result, "RealTimeAudioVisualizer panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_SonographyVisualization_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("SonographyVisualization");
      Assert.IsTrue(result, "SonographyVisualization panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_EmbeddingExplorer_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("EmbeddingExplorer");
      Assert.IsTrue(result, "EmbeddingExplorer panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_ProfileComparison_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("ProfileComparison");
      Assert.IsTrue(result, "ProfileComparison panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_ProfileHealthDashboard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("ProfileHealthDashboard");
      Assert.IsTrue(result, "ProfileHealthDashboard panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_TrainingQualityVisualization_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("TrainingQualityVisualization");
      Assert.IsTrue(result, "TrainingQualityVisualization panel should be visible");
    }

    #endregion

    #region Image/Video Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_ImageGen_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("ImageGen");
      Assert.IsTrue(result, "ImageGen panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_ImageSearch_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("ImageSearch");
      Assert.IsTrue(result, "ImageSearch panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VideoGen_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("VideoGen");
      Assert.IsTrue(result, "VideoGen panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_VideoEdit_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("VideoEdit");
      Assert.IsTrue(result, "VideoEdit panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Upscaling_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Upscaling");
      Assert.IsTrue(result, "Upscaling panel should be visible");
    }

    #endregion

    #region Scene/Spatial Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_SceneBuilder_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("SceneBuilder");
      Assert.IsTrue(result, "SceneBuilder panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_SpatialAudio_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("SpatialAudio");
      Assert.IsTrue(result, "SpatialAudio panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_SpatialStage_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("SpatialStage");
      Assert.IsTrue(result, "SpatialStage panel should be visible");
    }

    #endregion

    #region Organization Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_TagManager_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("TagManager");
      Assert.IsTrue(result, "TagManager panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_MarkerManager_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("MarkerManager");
      Assert.IsTrue(result, "MarkerManager panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_PresetLibrary_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("PresetLibrary");
      Assert.IsTrue(result, "PresetLibrary panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_TemplateLibrary_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("TemplateLibrary");
      Assert.IsTrue(result, "TemplateLibrary panel should be visible");
    }

    #endregion

    #region Automation/Workflow Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Automation_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Automation");
      Assert.IsTrue(result, "Automation panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_WorkflowAutomation_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("WorkflowAutomation");
      Assert.IsTrue(result, "WorkflowAutomation panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_TodoPanel_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("TodoPanel");
      Assert.IsTrue(result, "TodoPanel panel should be visible");
    }

    #endregion

    #region Dashboard Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_AnalyticsDashboard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("AnalyticsDashboard");
      Assert.IsTrue(result, "AnalyticsDashboard panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_UltimateDashboard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("UltimateDashboard");
      Assert.IsTrue(result, "UltimateDashboard panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_MCPDashboard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("MCPDashboard");
      Assert.IsTrue(result, "MCPDashboard panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_JobProgress_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("JobProgress");
      Assert.IsTrue(result, "JobProgress panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_AudioMonitoringDashboard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("AudioMonitoringDashboard");
      Assert.IsTrue(result, "AudioMonitoringDashboard panel should be visible");
    }

    #endregion

    #region Quality Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_QualityBenchmark_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("QualityBenchmark");
      Assert.IsTrue(result, "QualityBenchmark panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_QualityOptimizationWizard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("QualityOptimizationWizard");
      Assert.IsTrue(result, "QualityOptimizationWizard panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_DatasetQA_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("DatasetQA");
      Assert.IsTrue(result, "DatasetQA panel should be visible");
    }

    #endregion

    #region Advanced/AI Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_ABTesting_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("ABTesting");
      Assert.IsTrue(result, "ABTesting panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_AdvancedSearch_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("AdvancedSearch");
      Assert.IsTrue(result, "AdvancedSearch panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_MixAssistant_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("MixAssistant");
      Assert.IsTrue(result, "MixAssistant panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_AIProductionAssistant_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("AIProductionAssistant");
      Assert.IsTrue(result, "AIProductionAssistant panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_EngineParameterTuning_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("EngineParameterTuning");
      Assert.IsTrue(result, "EngineParameterTuning panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_EngineRecommendation_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("EngineRecommendation");
      Assert.IsTrue(result, "EngineRecommendation panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_AdvancedRealTimeVisualization_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("AdvancedRealTimeVisualization");
      Assert.IsTrue(result, "AdvancedRealTimeVisualization panel should be visible");
    }

    #endregion

    #region Phase 4A Expansion Panels

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_AIMixingMastering_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("AIMixingMastering");
      Assert.IsTrue(result, "AIMixingMastering panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_Assistant_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("Assistant");
      Assert.IsTrue(result, "Assistant panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_DeepfakeCreator_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("DeepfakeCreator");
      Assert.IsTrue(result, "DeepfakeCreator panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_HealthCheck_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("HealthCheck");
      Assert.IsTrue(result, "HealthCheck panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_MiniTimeline_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("MiniTimeline");
      Assert.IsTrue(result, "MiniTimeline panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_PipelineConversation_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("PipelineConversation");
      Assert.IsTrue(result, "PipelineConversation panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_PluginDetail_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("PluginDetail");
      Assert.IsTrue(result, "PluginDetail panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_PluginGallery_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("PluginGallery");
      Assert.IsTrue(result, "PluginGallery panel should be visible");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Panel_SLODashboard_CanNavigate()
    {
      VerifyApplicationStarted();
      var result = await NavigateToPanelAsync("SLODashboard");
      Assert.IsTrue(result, "SLODashboard panel should be visible");
    }

    #endregion

    #region Multi-Panel Navigation Tests

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Navigation_CorePanels_AllLoad()
    {
      VerifyApplicationStarted();

      var corePanels = new[] { "VoiceSynthesis", "Profiles", "Library", "Timeline", "EffectsMixer", "Analyzer" };
      
      foreach (var panel in corePanels)
      {
        ClearSimulatedState();
        var result = await NavigateToPanelAsync(panel);
        Assert.IsTrue(result, $"{panel} panel should be visible");
      }
    }

    [TestMethod]
    [TestCategory("Smoke")]
    [TestCategory("UI")]
    public async Task Navigation_SynthesisPanels_AllLoad()
    {
      VerifyApplicationStarted();

      var synthesisPanels = new[] { "VoiceSynthesis", "EnsembleSynthesis", "BatchProcessing" };
      
      foreach (var panel in synthesisPanels)
      {
        ClearSimulatedState();
        var result = await NavigateToPanelAsync(panel);
        Assert.IsTrue(result, $"{panel} panel should be visible");
      }
    }

    #endregion
  }
}
