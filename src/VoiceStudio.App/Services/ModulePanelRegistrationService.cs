using System;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Registers module panels that appear in the Modules menu but are not in CorePanelRegistrationService.
  /// </summary>
  public static class ModulePanelRegistrationService
  {
    /// <summary>
    /// Registers all module panels in the PanelRegistry.
    /// Called after AdvancedPanelRegistrationService and CorePanelRegistrationService.
    /// </summary>
    public static void RegisterModulePanels(IPanelRegistry registry)
    {
      if (registry == null)
        throw new ArgumentNullException(nameof(registry));

      // Voice
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VoiceCloningWizard",
        DisplayName = "Voice Cloning Wizard",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceCloningWizardView),
        ViewModelType = typeof(VoiceCloningWizardViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Experimental
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MultiVoiceGenerator",
        DisplayName = "Multi-Voice Generator",
        Region = PanelRegion.Center,
        ViewType = typeof(MultiVoiceGeneratorView),
        ViewModelType = typeof(MultiVoiceGeneratorViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Experimental
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "RealTimeConverter",
        DisplayName = "Real-Time Converter",
        Region = PanelRegion.Center,
        ViewType = typeof(RealTimeVoiceConverterView),
        ViewModelType = typeof(RealTimeVoiceConverterViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Experimental
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "EmotionStyle",
        DisplayName = "Emotion Style",
        Region = PanelRegion.Right,
        ViewType = typeof(EmotionStyleControlView),
        ViewModelType = typeof(EmotionStyleControlViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Multilingual",
        DisplayName = "Multilingual",
        Region = PanelRegion.Right,
        ViewType = typeof(MultilingualSupportView),
        ViewModelType = typeof(MultilingualSupportViewModel),
        MenuCategory = "Voice",
        Maturity = PanelMaturity.Experimental
      });

      // Analysis
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Spectrogram",
        DisplayName = "Spectrogram",
        Region = PanelRegion.Center,
        ViewType = typeof(SpectrogramView),
        ViewModelType = typeof(SpectrogramViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "RealTimeVisualizer",
        DisplayName = "Real-Time Visualizer",
        Region = PanelRegion.Center,
        ViewType = typeof(RealTimeAudioVisualizerView),
        ViewModelType = typeof(RealTimeAudioVisualizerViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Sonography",
        DisplayName = "Sonography",
        Region = PanelRegion.Center,
        ViewType = typeof(SonographyVisualizationView),
        ViewModelType = typeof(SonographyVisualizationViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "QualityOptimizer",
        DisplayName = "Quality Optimizer",
        Region = PanelRegion.Center,
        ViewType = typeof(QualityOptimizationWizardView),
        ViewModelType = typeof(QualityOptimizationWizardViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ABTesting",
        DisplayName = "A/B Testing",
        Region = PanelRegion.Center,
        ViewType = typeof(ABTestingView),
        ViewModelType = typeof(VoiceStudio.App.Views.Panels.ABTestingViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ProfileComparison",
        DisplayName = "Profile Comparison",
        Region = PanelRegion.Center,
        ViewType = typeof(ProfileComparisonView),
        ViewModelType = typeof(ProfileComparisonViewModel),
        MenuCategory = "Analysis",
        Maturity = PanelMaturity.Beta
      });

      // Media
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Upscaling",
        DisplayName = "Upscaling",
        Region = PanelRegion.Center,
        ViewType = typeof(UpscalingView),
        ViewModelType = typeof(UpscalingViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "ImageSearch",
        DisplayName = "Image Search",
        Region = PanelRegion.Left,
        ViewType = typeof(ImageSearchView),
        ViewModelType = typeof(ImageSearchViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Beta
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "VideoEdit",
        DisplayName = "Video Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(VideoEditView),
        ViewModelType = typeof(VideoEditViewModel),
        MenuCategory = "Media",
        Maturity = PanelMaturity.Beta
      });

      // Automation
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "Automation",
        DisplayName = "Automation",
        Region = PanelRegion.Center,
        ViewType = typeof(AutomationView),
        ViewModelType = typeof(AutomationViewModel),
        MenuCategory = "Automation",
        Maturity = PanelMaturity.Stable
      });

      // Management
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "PresetLibrary",
        DisplayName = "Presets",
        Region = PanelRegion.Left,
        ViewType = typeof(PresetLibraryView),
        ViewModelType = typeof(PresetLibraryViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TemplateLibrary",
        DisplayName = "Templates",
        Region = PanelRegion.Left,
        ViewType = typeof(TemplateLibraryView),
        ViewModelType = typeof(TemplateLibraryViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "TagManager",
        DisplayName = "Tags",
        Region = PanelRegion.Right,
        ViewType = typeof(TagManagerView),
        ViewModelType = typeof(TagManagerViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MarkerManager",
        DisplayName = "Markers",
        Region = PanelRegion.Right,
        ViewType = typeof(MarkerManagerView),
        ViewModelType = typeof(MarkerManagerViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "BackupRestore",
        DisplayName = "Backup & Restore",
        Region = PanelRegion.Center,
        ViewType = typeof(BackupRestoreView),
        ViewModelType = typeof(BackupRestoreViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "PluginManagement",
        DisplayName = "Plugins",
        Region = PanelRegion.Center,
        ViewType = typeof(PluginManagementView),
        ViewModelType = typeof(PluginManagementViewModel),
        MenuCategory = "Management",
        Maturity = PanelMaturity.Stable
      });

      // System
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "HealthCheck",
        DisplayName = "Health Check",
        Region = PanelRegion.Right,
        ViewType = typeof(HealthCheckView),
        ViewModelType = typeof(HealthCheckViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable
      });

      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "JobProgress",
        DisplayName = "Job Progress",
        Region = PanelRegion.Bottom,
        ViewType = typeof(JobProgressView),
        ViewModelType = typeof(JobProgressViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Stable
      });

      // F-10: Hidden - /api/mcp-dashboard is archived
      RegisterIfNotExists(registry, new PanelDescriptor
      {
        PanelId = "MCPDashboard",
        DisplayName = "MCP Dashboard",
        Region = PanelRegion.Center,
        ViewType = typeof(MCPDashboardView),
        ViewModelType = typeof(MCPDashboardViewModel),
        MenuCategory = "System",
        Maturity = PanelMaturity.Beta,
        IsVisible = false
      });
    }

    private static void RegisterIfNotExists(IPanelRegistry registry, PanelDescriptor descriptor)
    {
      if (!registry.IsRegistered(descriptor.PanelId))
      {
        registry.Register(descriptor);
      }
    }
  }
}
