using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  [TestClass]
  public class CorePanelRegistrationServiceTests
  {
    [TestMethod]
    public void RegisterCoreAndAdvancedPanels_RegistersCompleteUnifiedInventory()
    {
      var viewModelFactory = new Mock<IViewModelFactory>();
      var registry = new PanelRegistry(viewModelFactory.Object);

      CorePanelRegistrationService.RegisterCorePanels(registry);
      AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);

      Assert.AreEqual(
        98,
        registry.GetAllDescriptors().Count(),
        "Unified panel registry should include the full 96-panel inventory.");
    }

    [TestMethod]
    public void RegisterCorePanels_RegistersLegacyMigrationPanels()
    {
      var viewModelFactory = new Mock<IViewModelFactory>();
      var registry = new PanelRegistry(viewModelFactory.Object);

      CorePanelRegistrationService.RegisterCorePanels(registry);

      var registeredIds = new HashSet<string>(
        registry.GetAllDescriptors().Select(d => d.PanelId),
        StringComparer.OrdinalIgnoreCase);

      var expectedLegacyIds = new[]
      {
        "SpatialStage",
        "HealthCheck",
        "MiniTimeline",
        "JobProgress",
        "MCPDashboard",
        "SLODashboard",
        "AudioMonitoringDashboard",
        "AnalyticsDashboard",
        "UltimateDashboard",
        "BackupRestore",
        "PluginManagement",
        "PluginDetail",
        "PluginHealthDashboard",
        "ProfileComparison",
        "ProfileHealthDashboard",
        "ABTesting",
        "Spectrogram",
        "RealTimeAudioVisualizer",
        "SonographyVisualization",
        "AdvancedRealTimeVisualization",
        "Automation",
        "PipelineConversation",
        "PresetLibrary",
        "TemplateLibrary",
        "TagManager",
        "TagOrganization",
        "VoiceBrowser",
        "MarkerManager",
        "MixAssistant",
        "ImageSearch",
        "Upscaling",
        "VideoEdit",
        "ImageVideoEnhancementPipeline",
        "MultiVoiceGenerator",
        "RealTimeVoiceConverter",
        "TextHighlighting",
        "TextBasedSpeechEditor",
        "EmotionStyleControl",
        "EmotionStylePresetEditor",
        "MultilingualSupport",
        "AssistantView",
        "AdvancedSearch",
        "EngineParameterTuning",
        "EngineRecommendation",
        "TrainingQualityVisualization",
        "QualityOptimizationWizard",
        "LexiconView",
        "VoiceCloningWizard",
        "StyleTransfer",
        "AdvancedSpectrogramVisualization",
        "AdvancedWaveformVisualization"
      };

      foreach (var panelId in expectedLegacyIds)
      {
        Assert.IsTrue(
          registeredIds.Contains(panelId),
          $"Expected migrated panel '{panelId}' to be registered in CorePanelRegistrationService.");
      }
    }
  }
}
