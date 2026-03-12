using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.UI
{
  /// <summary>
  /// Base class for UI smoke tests providing common helper methods.
  /// Supports both simulated unit tests and real FlaUI automation scenarios.
  /// </summary>
  /// <remarks>
  /// Set UseRealAutomation to true in derived classes or via environment variable
  /// VOICESTUDIO_USE_REAL_UI_AUTOMATION=true to use FlaUI for real UI testing.
  /// </remarks>
  public abstract class SmokeTestBase : TestBase
  {
    /// <summary>
    /// Default timeout for panel visibility checks.
    /// </summary>
    protected static readonly TimeSpan DefaultPanelTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Default timeout for element visibility checks.
    /// </summary>
    protected static readonly TimeSpan DefaultElementTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Default polling interval for visibility checks.
    /// </summary>
    protected static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Backend base URL for health checks.
    /// </summary>
    protected const string BackendBaseUrl = "http://localhost:8000";

    /// <summary>
    /// Registry of panel names to their AutomationId patterns.
    /// Maps panel friendly name to AutomationId.
    /// Complete list of all 90 panels in VoiceStudio.
    /// </summary>
    protected static readonly Dictionary<string, string> PanelAutomationIds = new()
    {
      // Core panels
      { "VoiceSynthesis", "VoiceSynthesisView_Root" },
      { "Profiles", "ProfilesView_Root" },
      { "Library", "LibraryView_Root" },
      { "Timeline", "TimelineView_Root" },
      { "EffectsMixer", "EffectsMixerView_Root" },
      { "Analyzer", "AnalyzerView_Root" },
      { "Diagnostics", "DiagnosticsView_Root" },
      { "Macro", "MacroView_Root" },
      { "Settings", "SettingsView_Root" },
      
      // Synthesis panels
      { "EnsembleSynthesis", "EnsembleSynthesisView_Root" },
      { "BatchProcessing", "BatchProcessingView_Root" },
      { "MultiVoiceGenerator", "MultiVoiceGeneratorView_Root" },
      { "TextSpeechEditor", "TextSpeechEditorView_Root" },
      { "TextBasedSpeechEditor", "TextBasedSpeechEditorView_Root" },
      
      // Training panels
      { "Training", "TrainingView_Root" },
      { "TrainingDatasetEditor", "TrainingDatasetEditorView_Root" },
      { "TrainingQualityVisualization", "TrainingQualityVisualizationView_Root" },
      { "ModelManager", "ModelManagerView_Root" },
      { "DatasetQA", "DatasetQAView_Root" },
      
      // Audio panels
      { "Transcribe", "TranscribeView_Root" },
      { "Recording", "RecordingView_Root" },
      { "AudioAnalysis", "AudioAnalysisView_Root" },
      { "AudioMonitoringDashboard", "AudioMonitoringDashboardView_Root" },
      { "QualityControl", "QualityControlView_Root" },
      { "QualityDashboard", "QualityDashboardView_Root" },
      { "QualityBenchmark", "QualityBenchmarkView_Root" },
      { "QualityOptimizationWizard", "QualityOptimizationWizardView_Root" },
      
      // Settings/utility panels
      { "AdvancedSettings", "AdvancedSettingsView_Root" },
      { "KeyboardShortcuts", "KeyboardShortcutsView_Root" },
      { "PluginManagement", "PluginManagementView_Root" },
      { "APIKeyManager", "APIKeyManagerView_Root" },
      { "BackupRestore", "BackupRestoreView_Root" },
      
      // Help and status panels
      { "Help", "HelpView_Root" },
      { "GPUStatus", "GPUStatusView_Root" },
      { "JobProgress", "JobProgressView_Root" },
      { "MCPDashboard", "MCPDashboardView_Root" },
      { "AnalyticsDashboard", "AnalyticsDashboardView_Root" },
      { "UltimateDashboard", "UltimateDashboardView_Root" },
      
      // Voice control panels
      { "VoiceMorph", "VoiceMorphView_Root" },
      { "VoiceMorphingBlending", "VoiceMorphingBlendingView_Root" },
      { "VoiceBrowser", "VoiceBrowserView_Root" },
      { "VoiceQuickClone", "VoiceQuickCloneView_Root" },
      { "VoiceCloningWizard", "VoiceCloningWizardView_Root" },
      { "VoiceStyleTransfer", "VoiceStyleTransferView_Root" },
      { "StyleTransfer", "StyleTransferView_Root" },
      { "EmotionControl", "EmotionControlView_Root" },
      { "EmotionStyleControl", "EmotionStyleControlView_Root" },
      { "EmotionStylePresetEditor", "EmotionStylePresetEditorView_Root" },
      { "Prosody", "ProsodyView_Root" },
      { "SSML", "SSMLControlView_Root" },
      { "Lexicon", "LexiconView_Root" },
      { "PronunciationLexicon", "PronunciationLexiconView_Root" },
      { "MultilingualSupport", "MultilingualSupportView_Root" },
      { "RealTimeVoiceConverter", "RealTimeVoiceConverterView_Root" },
      
      // Visualization panels
      { "Spectrogram", "SpectrogramView_Root" },
      { "SonographyVisualization", "SonographyVisualizationView_Root" },
      { "RealTimeAudioVisualizer", "RealTimeAudioVisualizerView_Root" },
      { "AdvancedRealTimeVisualization", "AdvancedRealTimeVisualizationView_Root" },
      { "EmbeddingExplorer", "EmbeddingExplorerView_Root" },
      { "ProfileComparison", "ProfileComparisonView_Root" },
      { "ProfileHealthDashboard", "ProfileHealthDashboardView_Root" },
      
      // Image/video panels
      { "ImageGen", "ImageGenView_Root" },
      { "ImageSearch", "ImageSearchView_Root" },
      { "VideoGen", "VideoGenView_Root" },
      { "VideoEdit", "VideoEditView_Root" },
      { "Upscaling", "UpscalingView_Root" },
      { "ImageVideoEnhancementPipeline", "ImageVideoEnhancementPipelineView_Root" },
      
      // Scene and spatial panels
      { "SceneBuilder", "SceneBuilderView_Root" },
      { "SpatialAudio", "SpatialAudioView_Root" },
      { "SpatialStage", "SpatialStageView_Root" },
      
      // Organization panels
      { "TagManager", "TagManagerView_Root" },
      { "TagOrganization", "TagOrganizationView_Root" },
      { "MarkerManager", "MarkerManagerView_Root" },
      { "PresetLibrary", "PresetLibraryView_Root" },
      { "TemplateLibrary", "TemplateLibraryView_Root" },
      
      // Script and automation panels
      { "ScriptEditor", "ScriptEditorView_Root" },
      { "Automation", "AutomationView_Root" },
      { "WorkflowAutomation", "WorkflowAutomationView_Root" },
      { "TodoPanel", "TodoPanelView_Root" },
      
      // Advanced panels
      { "ABTesting", "ABTestingView_Root" },
      { "AdvancedSearch", "AdvancedSearchView_Root" },
      { "MixAssistant", "MixAssistantView_Root" },
      { "AIProductionAssistant", "AIProductionAssistantView_Root" },
      { "EngineParameterTuning", "EngineParameterTuningView_Root" },
      { "EngineRecommendation", "EngineRecommendationView_Root" },
      { "TextHighlighting", "TextHighlightingView_Root" },
      
      // Additional panels (Phase 4A expansion)
      { "AIMixingMastering", "AIMixingMasteringView_Root" },
      { "Assistant", "AssistantView_Root" },
      { "DeepfakeCreator", "DeepfakeCreatorView_Root" },
      { "HealthCheck", "HealthCheckView_Root" },
      { "MiniTimeline", "MiniTimelineView_Root" },
      { "PipelineConversation", "PipelineConversationView_Root" },
      { "PluginDetail", "PluginDetailView_Root" },
      { "PluginGallery", "PluginGalleryView_Root" },
      { "SLODashboard", "SLODashboardView_Root" },
    };

    /// <summary>
    /// Simulated visibility state for unit testing without UI automation.
    /// Only used when UseRealAutomation is false.
    /// </summary>
    private readonly HashSet<string> _simulatedVisiblePanels = new();

    /// <summary>
    /// FlaUI app launcher for real UI automation.
    /// </summary>
    private AppLauncher? _appLauncher;

    /// <summary>
    /// The main application window when using real automation.
    /// </summary>
    protected Window? MainWindow { get; private set; }

    /// <summary>
    /// Gets whether real UI automation is enabled.
    /// Override in derived classes or set VOICESTUDIO_USE_REAL_UI_AUTOMATION=true.
    /// </summary>
    protected virtual bool UseRealAutomation
    {
      get
      {
        var envValue = Environment.GetEnvironmentVariable("VOICESTUDIO_USE_REAL_UI_AUTOMATION");
        return string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(envValue, "1", StringComparison.OrdinalIgnoreCase);
      }
    }

    /// <summary>
    /// Directory for storing screenshots on test failure.
    /// </summary>
    protected virtual string ScreenshotDirectory => Path.Combine(
      Environment.GetEnvironmentVariable("VOICESTUDIO_TEST_ARTIFACTS") ?? ".buildlogs",
      "screenshots");

    /// <summary>
    /// Test execution log for debugging.
    /// </summary>
    protected List<string> ExecutionLog { get; } = new();

    /// <summary>
    /// Record a log entry for test debugging.
    /// </summary>
    protected void Log(string message)
    {
      var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
      ExecutionLog.Add(entry);
      TestContext?.WriteLine(entry);
    }

    /// <summary>
    /// Waits for a panel to become visible with timeout.
    /// </summary>
    /// <param name="panelName">Panel name or AutomationId.</param>
    /// <param name="timeout">Optional timeout (defaults to 5 seconds).</param>
    /// <returns>True if panel became visible, false if timed out.</returns>
    protected async Task<bool> WaitForPanelAsync(string panelName, TimeSpan? timeout = null)
    {
      var effectiveTimeout = timeout ?? DefaultPanelTimeout;
      var automationId = GetAutomationId(panelName);
      
      Log($"WaitForPanel: {panelName} (AutomationId: {automationId}, Timeout: {effectiveTimeout.TotalSeconds}s, RealAutomation: {UseRealAutomation})");
      
      var sw = Stopwatch.StartNew();
      while (sw.Elapsed < effectiveTimeout)
      {
        if (IsPanelVisible(automationId))
        {
          Log($"WaitForPanel: {panelName} visible after {sw.ElapsedMilliseconds}ms");
          return true;
        }
        
        await Task.Delay(DefaultPollingInterval);
      }
      
      Log($"WaitForPanel: {panelName} TIMED OUT after {sw.ElapsedMilliseconds}ms");
      return false;
    }

    /// <summary>
    /// Checks if a panel is currently visible.
    /// Uses FlaUI when UseRealAutomation is true, otherwise uses simulated state.
    /// </summary>
    protected bool IsPanelVisible(string automationId)
    {
      if (UseRealAutomation)
      {
        // Real UI automation using FlaUI
        if (MainWindow == null)
        {
          Log($"IsPanelVisible: MainWindow is null, returning false");
          return false;
        }

        try
        {
          var element = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
          var isVisible = element != null && element.IsAvailable && element.IsOffscreen == false;
          Log($"IsPanelVisible (FlaUI): {automationId} = {isVisible}");
          return isVisible;
        }
        catch (Exception ex)
        {
          Log($"IsPanelVisible (FlaUI): Error querying {automationId} - {ex.Message}");
          return false;
        }
      }
      else
      {
        // Simulated mode for unit testing
        return _simulatedVisiblePanels.Contains(automationId);
      }
    }

    /// <summary>
    /// Finds an element by AutomationId.
    /// </summary>
    /// <param name="automationId">The AutomationId to search for.</param>
    /// <returns>The element if found, null otherwise.</returns>
    protected AutomationElement? FindElement(string automationId)
    {
      if (!UseRealAutomation || MainWindow == null)
      {
        return null;
      }

      try
      {
        return MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
      }
      catch (Exception ex)
      {
        Log($"FindElement: Error finding {automationId} - {ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Simulates panel visibility for unit testing.
    /// Only used when UseRealAutomation is false.
    /// </summary>
    protected void SimulatePanelVisible(string panelName)
    {
      var automationId = GetAutomationId(panelName);
      _simulatedVisiblePanels.Add(automationId);
      Log($"SimulatePanelVisible: {panelName} -> {automationId}");
    }

    /// <summary>
    /// Simulates panel hidden for unit testing.
    /// Only used when UseRealAutomation is false.
    /// </summary>
    protected void SimulatePanelHidden(string panelName)
    {
      var automationId = GetAutomationId(panelName);
      _simulatedVisiblePanels.Remove(automationId);
      Log($"SimulatePanelHidden: {panelName} -> {automationId}");
    }

    /// <summary>
    /// Gets the AutomationId for a panel, or returns the input if already an AutomationId.
    /// </summary>
    private static string GetAutomationId(string panelNameOrId)
    {
      if (panelNameOrId.EndsWith("_Root"))
        return panelNameOrId;
      
      return PanelAutomationIds.TryGetValue(panelNameOrId, out var automationId) 
        ? automationId 
        : $"{panelNameOrId}View_Root";
    }

    /// <summary>
    /// Clicks a button by AutomationId.
    /// Uses FlaUI when UseRealAutomation is true.
    /// </summary>
    /// <param name="automationId">Button AutomationId.</param>
    /// <param name="simulateDelay">Delay after click (used in simulated mode).</param>
    protected async Task ClickButtonAsync(string automationId, TimeSpan? simulateDelay = null)
    {
      Log($"ClickButton: {automationId} (RealAutomation: {UseRealAutomation})");

      if (UseRealAutomation && MainWindow != null)
      {
        try
        {
          var button = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsButton();
          if (button != null)
          {
            button.Invoke();
            Log($"ClickButton: {automationId} clicked via FlaUI");
            await Task.Delay(TimeSpan.FromMilliseconds(100)); // Allow UI to update
          }
          else
          {
            Log($"ClickButton: {automationId} not found");
          }
        }
        catch (Exception ex)
        {
          Log($"ClickButton: Error clicking {automationId} - {ex.Message}");
        }
      }
      else
      {
        // Simulated mode
        await Task.Delay(simulateDelay ?? TimeSpan.FromMilliseconds(50));
        Log($"ClickButton: {automationId} complete (simulated)");
      }
    }

    /// <summary>
    /// Enters text into a control by AutomationId.
    /// Uses FlaUI when UseRealAutomation is true.
    /// </summary>
    /// <param name="automationId">Control AutomationId.</param>
    /// <param name="text">Text to enter.</param>
    protected async Task EnterTextAsync(string automationId, string text)
    {
      Log($"EnterText: {automationId} <- '{text}' (RealAutomation: {UseRealAutomation})");

      if (UseRealAutomation && MainWindow != null)
      {
        try
        {
          var textBox = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsTextBox();
          if (textBox != null)
          {
            textBox.Text = text;
            Log($"EnterText: {automationId} set via FlaUI");
            await Task.Delay(TimeSpan.FromMilliseconds(50)); // Allow UI to update
          }
          else
          {
            Log($"EnterText: {automationId} not found");
          }
        }
        catch (Exception ex)
        {
          Log($"EnterText: Error setting text on {automationId} - {ex.Message}");
        }
      }
      else
      {
        // Simulated mode
        await Task.Delay(50);
        Log($"EnterText: {automationId} complete (simulated)");
      }
    }

    /// <summary>
    /// Waits for a UI element to become available.
    /// </summary>
    /// <param name="automationId">Element AutomationId.</param>
    /// <param name="timeout">Optional timeout.</param>
    protected async Task<bool> WaitForElementAsync(string automationId, TimeSpan? timeout = null)
    {
      var effectiveTimeout = timeout ?? DefaultElementTimeout;
      Log($"WaitForElement: {automationId} (Timeout: {effectiveTimeout.TotalSeconds}s, RealAutomation: {UseRealAutomation})");
      
      var sw = Stopwatch.StartNew();
      while (sw.Elapsed < effectiveTimeout)
      {
        if (UseRealAutomation && MainWindow != null)
        {
          var element = FindElement(automationId);
          if (element != null && element.IsAvailable)
          {
            Log($"WaitForElement: {automationId} found after {sw.ElapsedMilliseconds}ms");
            return true;
          }
        }
        else
        {
          // Simulated mode - return true after a short delay
          await Task.Delay(DefaultPollingInterval);
          Log($"WaitForElement: {automationId} found after {sw.ElapsedMilliseconds}ms (simulated)");
          return true;
        }
        
        await Task.Delay(DefaultPollingInterval);
      }
      
      Log($"WaitForElement: {automationId} TIMED OUT after {sw.ElapsedMilliseconds}ms");
      return false;
    }

    /// <summary>
    /// Verifies that the application has started successfully.
    /// </summary>
    protected void VerifyApplicationStarted()
    {
      Assert.IsNotNull(TestContext, "TestContext should be available");
      Log($"VerifyApplicationStarted: TestContext available (RealAutomation: {UseRealAutomation})");
      
      if (UseRealAutomation)
      {
        Assert.IsNotNull(MainWindow, "MainWindow should be available when using real automation");
        Assert.IsTrue(MainWindow!.IsAvailable, "MainWindow should be available");
        Log("VerifyApplicationStarted: MainWindow verified via FlaUI");
      }
    }

    /// <summary>
    /// Checks if the backend is healthy and responding.
    /// </summary>
    /// <param name="timeout">Optional timeout for the health check.</param>
    /// <returns>True if backend is healthy, false otherwise.</returns>
    protected async Task<bool> IsBackendHealthyAsync(TimeSpan? timeout = null)
    {
      var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
      Log($"IsBackendHealthy: Checking {BackendBaseUrl}/health");
      
      try
      {
        using var cts = new CancellationTokenSource(effectiveTimeout);
        using var client = new HttpClient { Timeout = effectiveTimeout };
        
        var response = await client.GetAsync($"{BackendBaseUrl}/health", cts.Token);
        var isHealthy = response.IsSuccessStatusCode;
        
        Log($"IsBackendHealthy: {(isHealthy ? "HEALTHY" : "UNHEALTHY")} (Status: {(int)response.StatusCode})");
        return isHealthy;
      }
      catch (Exception ex)
      {
        Log($"IsBackendHealthy: ERROR - {ex.GetType().Name}: {ex.Message}");
        return false;
      }
    }

    /// <summary>
    /// Navigates to a panel and waits for it to load.
    /// </summary>
    /// <param name="panelName">Panel to navigate to.</param>
    /// <param name="simulateSuccess">In simulated mode, whether to simulate successful navigation.</param>
    protected async Task<bool> NavigateToPanelAsync(string panelName, bool simulateSuccess = true)
    {
      Log($"NavigateToPanel: {panelName} (RealAutomation: {UseRealAutomation})");

      if (UseRealAutomation)
      {
        // In real automation, we need to find and click the navigation button
        // Navigation buttons typically have AutomationId like "Nav_{panelName}" or "{panelName}_NavButton"
        var navButtonIds = new[]
        {
          $"Nav_{panelName}",
          $"{panelName}_NavButton",
          $"NavButton_{panelName}",
          $"MenuItem_{panelName}"
        };

        foreach (var buttonId in navButtonIds)
        {
          var button = FindElement(buttonId);
          if (button != null)
          {
            await ClickButtonAsync(buttonId);
            break;
          }
        }
      }
      else if (simulateSuccess)
      {
        SimulatePanelVisible(panelName);
      }
      
      return await WaitForPanelAsync(panelName);
    }

    /// <summary>
    /// Gets the list of all registered panel names.
    /// </summary>
    protected static IEnumerable<string> GetAllPanelNames() => PanelAutomationIds.Keys;

    /// <summary>
    /// Creates a mock IProfileQualityInsightsService for unit tests.
    /// </summary>
    protected static IProfileQualityInsightsService CreateMockProfileQualityInsightsService()
    {
      var mock = new Mock<IProfileQualityInsightsService>();
      mock.Setup(x => x.LoadQualityHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceStudio.Core.Models.QualityHistoryEntry>());
      mock.Setup(x => x.LoadQualityTrendsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceStudio.Core.Models.QualityTrends());
      mock.Setup(x => x.LoadQualityBaselineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((VoiceStudio.Core.Models.QualityBaseline?)null);
      mock.Setup(x => x.GetQualityDegradationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((VoiceStudio.Core.Models.QualityDegradationResponse?)null);
      return mock.Object;
    }

    /// <summary>
    /// Creates a mock IProfileTransferService for unit tests.
    /// </summary>
    protected static IProfileTransferService CreateMockProfileTransferService()
    {
      var mock = new Mock<IProfileTransferService>();
      mock.Setup(x => x.ParseImports(It.IsAny<string>()))
        .Returns((new List<ProfileImportData>(), (string?)null));
      mock.Setup(x => x.CreateProfilesFromImportDataAsync(It.IsAny<IReadOnlyList<ProfileImportData>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());
      mock.Setup(x => x.BuildExportJson(It.IsAny<IEnumerable<VoiceProfile>>()))
        .Returns("{}");
      mock.Setup(x => x.SanitizeFilename(It.IsAny<string?>()))
        .Returns((string? v) => string.IsNullOrWhiteSpace(v) ? "profile_export" : v);
      return mock.Object;
    }

    /// <summary>
    /// Creates a mock IProfileEnhancementService for unit tests.
    /// </summary>
    protected static IProfileEnhancementService CreateMockProfileEnhancementService()
    {
      var mock = new Mock<IProfileEnhancementService>();
      mock.Setup(x => x.EnhanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((VoiceStudio.Core.Models.ReferenceAudioPreprocessResponse?)null);
      return mock.Object;
    }

    /// <summary>
    /// Clears all simulated panel states.
    /// </summary>
    protected void ClearSimulatedState()
    {
      _simulatedVisiblePanels.Clear();
      Log("ClearSimulatedState: All panel states cleared");
    }

    /// <summary>
    /// Captures a screenshot if using real automation.
    /// </summary>
    /// <param name="testName">Name of the test for the screenshot filename.</param>
    protected void CaptureScreenshotOnFailure(string? testName = null)
    {
      if (!UseRealAutomation || _appLauncher == null)
      {
        return;
      }

      try
      {
        var fileName = $"{testName ?? TestContext?.TestName ?? "test"}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var filePath = Path.Combine(ScreenshotDirectory, fileName);
        
        if (_appLauncher.CaptureScreenshot(filePath))
        {
          Log($"Screenshot captured: {filePath}");
          TestContext?.AddResultFile(filePath);
        }
        else
        {
          Log("Screenshot capture failed");
        }
      }
      catch (Exception ex)
      {
        Log($"Screenshot capture error: {ex.Message}");
      }
    }

    /// <summary>
    /// Launches the application when using real automation.
    /// </summary>
    protected virtual async Task LaunchApplicationAsync()
    {
      if (!UseRealAutomation)
      {
        Log("LaunchApplication: Skipped (simulated mode)");
        return;
      }

      Log("LaunchApplication: Starting VoiceStudio with FlaUI...");
      _appLauncher = new AppLauncher();
      
      try
      {
        MainWindow = await _appLauncher.LaunchAsync(timeout: TimeSpan.FromSeconds(60));
        Log($"LaunchApplication: VoiceStudio started, MainWindow available");
      }
      catch (Exception ex)
      {
        Log($"LaunchApplication: Failed - {ex.Message}");
        throw;
      }
    }

    /// <summary>
    /// Closes the application when using real automation.
    /// </summary>
    protected virtual async Task CloseApplicationAsync()
    {
      if (_appLauncher == null)
      {
        return;
      }

      Log("CloseApplication: Closing VoiceStudio...");
      var closedGracefully = await _appLauncher.CloseAsync(TimeSpan.FromSeconds(10));
      Log($"CloseApplication: {(closedGracefully ? "Closed gracefully" : "Force killed")}");
    }

    /// <inheritdoc/>
    public override void TestInitialize()
    {
      base.TestInitialize();
      ExecutionLog.Clear();
      ClearSimulatedState();
      
      Log($"TestInitialize: UseRealAutomation = {UseRealAutomation}");
      
      if (UseRealAutomation)
      {
        // Launch app synchronously during initialization
        LaunchApplicationAsync().GetAwaiter().GetResult();
      }
    }

    /// <inheritdoc/>
    public override void TestCleanup()
    {
      // Capture screenshot on failure
      if (TestContext?.CurrentTestOutcome == UnitTestOutcome.Failed)
      {
        CaptureScreenshotOnFailure();
        
        TestContext.WriteLine("=== Smoke Test Execution Log ===");
        foreach (var entry in ExecutionLog)
        {
          TestContext.WriteLine(entry);
        }
      }

      // Close app if using real automation
      if (UseRealAutomation)
      {
        CloseApplicationAsync().GetAwaiter().GetResult();
      }

      _appLauncher?.Dispose();
      _appLauncher = null;
      MainWindow = null;
      
      base.TestCleanup();
    }
  }
}
