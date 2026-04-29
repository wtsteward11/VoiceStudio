// =============================================================================
// AutomationIds.cs - Centralized UI Automation Identifiers
// =============================================================================
// 
// This file provides a single source of truth for all UI automation identifiers
// used in VoiceStudio. These IDs are referenced by:
// - XAML views (AutomationProperties.AutomationId)
// - UI tests (WinAppDriver element lookups)
// - Accessibility tools
//
// NAMING CONVENTION:
//   {ViewName}_{ElementType}_{Purpose}
//
// Examples:
//   VoiceSynthesisView_Button_Synthesize
//   LibraryView_ListView_Files
//   DiagnosticsView_TabView_Main
//
// IMPORTANT: When adding new IDs:
// 1. Add the constant here first
// 2. Reference it in XAML using x:Static or as string
// 3. Update tests/ui/fixtures/automation_ids.py if needed
// =============================================================================

namespace VoiceStudio.App.Constants;

/// <summary>
/// Centralized automation identifiers for UI testing and accessibility.
/// </summary>
public static class AutomationIds
{
    // =========================================================================
    // Navigation
    // =========================================================================
    
    public static class Navigation
    {
        public const string NavStudio = "NavStudio";
        public const string NavGenerate = "NavGenerate";
        public const string NavSynthesize = "NavSynthesize";
        public const string NavTTS = "NavTTS";
        public const string NavTranscribe = "NavTranscribe";
        public const string NavSTT = "NavSTT";
        public const string NavCloning = "NavCloning";
        public const string NavVoiceCloning = "NavVoiceCloning";
        public const string NavClone = "NavClone";
        public const string NavProfiles = "NavProfiles";
        public const string NavVoices = "NavVoices";
        public const string NavLibrary = "NavLibrary";
        public const string NavFiles = "NavFiles";
        public const string NavTimeline = "NavTimeline";
        public const string NavAnalyzer = "NavAnalyzer";
        public const string NavEffects = "NavEffects";
        public const string NavMixer = "NavMixer";
        public const string NavSettings = "NavSettings";
        public const string NavDiagnostics = "NavDiagnostics";
        public const string NavDebug = "NavDebug";
        public const string NavHealth = "NavHealth";
        public const string NavTraining = "NavTraining";
        public const string NavBatch = "NavBatch";
        public const string NavRealtime = "NavRealtime";
        public const string NavRecording = "NavRecording";
    }
    
    // =========================================================================
    // Voice Synthesis Panel
    // =========================================================================
    
    public static class VoiceSynthesis
    {
        public const string Root = "VoiceSynthesisView_Root";
        public const string ProfileComboBox = "VoiceSynthesisView_ProfileComboBox";
        public const string EngineComboBox = "VoiceSynthesisView_EngineComboBox";
        public const string LanguageComboBox = "VoiceSynthesisView_LanguageComboBox";
        public const string EmotionComboBox = "VoiceSynthesisView_EmotionComboBox";
        public const string TextInput = "VoiceSynthesisView_TextInput";
        public const string SynthesizeButton = "VoiceSynthesisView_SynthesizeButton";
        public const string StreamButton = "VoiceSynthesisView_StreamButton";
        public const string PlayButton = "VoiceSynthesisView_PlayButton";
        public const string StopButton = "VoiceSynthesisView_StopButton";
        public const string AddToTimelineButton = "VoiceSynthesisView_AddToTimelineButton";
        public const string CopyWorkflowEvidenceButton = "VoiceSynthesisView_CopyWorkflowEvidenceButton";
        public const string AnalyzeButton = "VoiceSynthesisView_AnalyzeButton";
        public const string RefreshButton = "VoiceSynthesisView_RefreshButton";
        public const string HelpButton = "VoiceSynthesisView_HelpButton";
    }
    
    // =========================================================================
    // Transcription Panel
    // =========================================================================
    
    public static class Transcribe
    {
        public const string Root = "TranscribeView_Root";
        public const string AudioIdInput = "TranscribeView_AudioIdInput";
        public const string ProjectIdInput = "TranscribeView_ProjectIdInput";
        public const string EngineComboBox = "TranscribeView_EngineComboBox";
        public const string LanguageComboBox = "TranscribeView_LanguageComboBox";
        public const string TranscribeButton = "TranscribeView_TranscribeButton";
        public const string WordTimestampsToggle = "TranscribeView_WordTimestampsToggle";
        public const string DiarizationToggle = "TranscribeView_DiarizationToggle";
        public const string VadToggle = "TranscribeView_VadToggle";
        public const string TranscriptionList = "TranscribeView_TranscriptionList";
        public const string TextDisplay = "TranscribeView_TextDisplay";
        /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-JOB-STATUS-01: session apply/regenerate job status list.</summary>
        public const string ApplyJobStatusList = "TranscribeView_ApplyJobStatusList";
        /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01: retry failed apply/regenerate job row.</summary>
        public const string ApplyJobRetryButton = "TranscribeView_ApplyJobRetryButton";
        /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-JOB-STATUS-01: clear session job status rows.</summary>
        public const string ClearApplyJobStatusButton = "TranscribeView_ClearApplyJobStatusButton";
    }
    
    // =========================================================================
    // Voice Cloning Panel
    // =========================================================================
    
    public static class VoiceCloning
    {
        public const string Root = "VoiceCloningWizardView_Root";
        public const string WizardSteps = "VoiceCloningWizardView_WizardSteps";
        public const string ReferenceUpload = "VoiceCloningWizardView_ReferenceUpload";
        public const string CloneButton = "VoiceCloningWizardView_CloneButton";
        public const string QuickCloneRoot = "VoiceQuickCloneView_Root";
    }
    
    // =========================================================================
    // Library Panel
    // =========================================================================
    
    public static class Library
    {
        public const string Root = "LibraryView_Root";
        public const string FileList = "LibraryView_FileList";
        public const string FoldersListView = "LibraryView_FoldersListView";
        public const string SearchBox = "LibraryView_SearchBox";
        public const string FilterButton = "LibraryView_FilterButton";
        public const string ImportButton = "LibraryView_ImportButton";
        public const string DragDropCanvas = "LibraryView_DragDropCanvas";
        /// <summary>GAP-027: Context flyout — send selected library audio to timeline.</summary>
        public const string MenuAddToTimeline = "LibraryView_Menu_AddToTimeline";
    }
    
    // =========================================================================
    // Profiles Panel
    // =========================================================================
    
    public static class Profiles
    {
        public const string Root = "ProfilesView_Root";
        public const string ProfileList = "ProfilesView_ProfileList";
        public const string AddProfileButton = "ProfilesView_AddProfileButton";
        public const string ImportButton = "ProfilesView_ImportButton";
    }
    
    // =========================================================================
    // Timeline Panel
    // =========================================================================
    
    public static class Timeline
    {
        public const string Root = "TimelineView_Root";
        public const string TrackList = "TimelineView_TrackList";
        public const string PlayButton = "TimelineView_PlayButton";
        public const string StopButton = "TimelineView_StopButton";
        public const string ExportButton = "TimelineView_ExportButton";
    }
    
    // =========================================================================
    // Analyzer Panel
    // =========================================================================
    
    public static class Analyzer
    {
        public const string Root = "AnalyzerView_Root";
        public const string WaveformDisplay = "AnalyzerView_WaveformDisplay";
        public const string SpectrumDisplay = "AnalyzerView_SpectrumDisplay";
        public const string AnalyzeButton = "AnalyzerView_AnalyzeButton";
    }
    
    // =========================================================================
    // Effects Mixer Panel
    // =========================================================================
    
    public static class EffectsMixer
    {
        public const string Root = "EffectsMixerView_Root";
        public const string EffectsList = "EffectsMixerView_EffectsList";
        public const string ApplyButton = "EffectsMixerView_ApplyButton";
        public const string ExportButton = "EffectsMixerView_ExportButton";
        public const string PresetComboBox = "EffectsMixerView_PresetComboBox";
        /// <summary>GAP-039: Master bypass for effect chain process (bypass_chain query).</summary>
        public const string EffectChainBypassToggle = "EffectsMixerView_EffectChainBypassToggle";
    }
    
    // =========================================================================
    // Diagnostics Panel
    // =========================================================================
    
    public static class Diagnostics
    {
        public const string Root = "DiagnosticsView_Root";
        public const string TabView = "DiagnosticsView_TabView";
        public const string ActiveJobsListView = "DiagnosticsView_ActiveJobsListView";
        public const string LogLevelFilter = "DiagnosticsView_LogLevelFilter";
        public const string LogSearchBox = "DiagnosticsView_LogSearchBox";
        public const string LogsListView = "DiagnosticsView_LogsListView";
        public const string TestConnectionButton = "DiagnosticsView_TestConnectionButton";
        public const string RefreshEnginesButton = "DiagnosticsView_RefreshEnginesButton";
    }
    
    // =========================================================================
    // Settings Panel
    // =========================================================================
    
    public static class Settings
    {
        public const string Root = "SettingsView_Root";
        public const string SettingsCategories = "SettingsView_SettingsCategories";
        public const string SaveButton = "SettingsView_SaveButton";
    }
    
    // =========================================================================
    // Training Panel
    // =========================================================================
    
    public static class Training
    {
        public const string Root = "TrainingView_Root";
        public const string DatasetSelector = "TrainingView_DatasetSelector";
        public const string ModelConfig = "TrainingView_ModelConfig";
        public const string TrainButton = "TrainingView_TrainButton";
    }
    
    // =========================================================================
    // Real-Time Voice Converter Panel
    // =========================================================================
    
    public static class RealTimeVoiceConverter
    {
        public const string Root = "RealTimeVoiceConverterView_Root";
        public const string InputDevice = "RealTimeVoiceConverterView_InputDevice";
        public const string OutputDevice = "RealTimeVoiceConverterView_OutputDevice";
        public const string StartButton = "RealTimeVoiceConverterView_StartButton";
    }
    
    // =========================================================================
    // Common Dialog Elements
    // =========================================================================
    
    public static class Dialogs
    {
        public const string ConfirmButton = "Dialog_ConfirmButton";
        public const string CancelButton = "Dialog_CancelButton";
        public const string CloseButton = "Dialog_CloseButton";
        public const string ContentArea = "Dialog_ContentArea";
    }
    
    // =========================================================================
    // Status Indicators
    // =========================================================================
    
    public static class Status
    {
        public const string BackendStatus = "Status_BackendConnection";
        public const string GPUStatus = "Status_GPUAvailability";
        public const string JobProgress = "Status_JobProgress";
        public const string EngineStatus = "Status_EngineStatus";
    }
}
