using System;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service to register all 9 advanced panels in the PanelRegistry.
  /// </summary>
  public static class AdvancedPanelRegistrationService
  {
    /// <summary>
    /// Registers all 9 advanced panels in the PanelRegistry.
    /// </summary>
    public static void RegisterAdvancedPanels(IPanelRegistry registry)
    {
      if (registry == null)
        throw new ArgumentNullException(nameof(registry));

      // Panel 1: Text-Based Speech Editor (Pro)
      registry.Register(new PanelDescriptor
      {
        PanelId = "TextSpeechEditor",
        DisplayName = "Text Speech Editor",
        Region = PanelRegion.Center,
        ViewType = typeof(TextSpeechEditorView),
        ViewModelType = typeof(TextSpeechEditorViewModel)
      });

      // Panel 2: Prosody & Phoneme Control (Advanced)
      registry.Register(new PanelDescriptor
      {
        PanelId = "Prosody",
        DisplayName = "Prosody & Phoneme Control",
        Region = PanelRegion.Center,
        ViewType = typeof(ProsodyView),
        ViewModelType = typeof(ProsodyViewModel)
      });

      // Panel 3: Spatial Audio (Pro)
      registry.Register(new PanelDescriptor
      {
        PanelId = "SpatialAudio",
        DisplayName = "Spatial Audio",
        Region = PanelRegion.Right,
        ViewType = typeof(SpatialAudioView),
        ViewModelType = typeof(SpatialAudioViewModel)
      });

      // Panel 4: AI Mixing & Mastering Assistant (Pro)
      registry.Register(new PanelDescriptor
      {
        PanelId = "AIMixingMastering",
        DisplayName = "AI Mixing & Mastering",
        Region = PanelRegion.Right,
        ViewType = typeof(AIMixingMasteringView),
        ViewModelType = typeof(AIMixingMasteringViewModel)
      });

      // Panel 5: Voice Style Transfer (Pro)
      registry.Register(new PanelDescriptor
      {
        PanelId = "VoiceStyleTransfer",
        DisplayName = "Voice Style Transfer",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceStyleTransferView),
        ViewModelType = typeof(VoiceStyleTransferViewModel)
      });

      // Panel 6: Speaker Embedding Explorer (Technical)
      registry.Register(new PanelDescriptor
      {
        PanelId = "EmbeddingExplorer",
        DisplayName = "Speaker Embedding Explorer",
        Region = PanelRegion.Right,
        ViewType = typeof(EmbeddingExplorerView),
        ViewModelType = typeof(EmbeddingExplorerViewModel)
      });

      // Panel 7: AI Production Assistant (Meta)
      registry.Register(new PanelDescriptor
      {
        PanelId = "AIProductionAssistant",
        DisplayName = "AI Production Assistant",
        Region = PanelRegion.Right,
        ViewType = typeof(AIProductionAssistantView),
        ViewModelType = typeof(AIProductionAssistantViewModel)
      });

      // Panel 8: Pronunciation Lexicon (Advanced)
      registry.Register(new PanelDescriptor
      {
        PanelId = "PronunciationLexicon",
        DisplayName = "Pronunciation Lexicon",
        Region = PanelRegion.Right,
        ViewType = typeof(PronunciationLexiconView),
        ViewModelType = typeof(PronunciationLexiconViewModel)
      });

      // Panel 9: Voice Morphing/Blending (Pro)
      registry.Register(new PanelDescriptor
      {
        PanelId = "VoiceMorphingBlending",
        DisplayName = "Voice Morphing/Blending",
        Region = PanelRegion.Center,
        ViewType = typeof(VoiceMorphingBlendingView),
        ViewModelType = typeof(VoiceMorphingBlendingViewModel)
      });

      // Panel 10: Plugin Gallery
      registry.Register(new PanelDescriptor
      {
        PanelId = "PluginGallery",
        DisplayName = "Plugin Gallery",
        Region = PanelRegion.Center,
        ViewType = typeof(PluginGalleryView),
        ViewModelType = typeof(PluginGalleryViewModel)
      });

      // Panel 11: Theme Editor
      registry.Register(new PanelDescriptor
      {
        PanelId = "ThemeEditor",
        DisplayName = "Theme Editor",
        Region = PanelRegion.Right,
        ViewType = typeof(ThemeEditorView)
      });
    }
  }
}
