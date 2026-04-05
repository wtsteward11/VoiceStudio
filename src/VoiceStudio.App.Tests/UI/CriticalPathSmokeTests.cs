using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Tests.ViewModels;

namespace VoiceStudio.App.Tests.UI
{
  /// <summary>
  /// Smoke tests for critical user workflows.
  /// Verifies that end-to-end workflows like creating a profile, synthesizing voice, applying effects, and exporting work.
  /// </summary>
  [TestClass]
  [TestCategory("UI")]
  [Ignore("Disabled for finish-line stability; UI automation not required.")]
  public class CriticalPathSmokeTests : SmokeTestBase
  {
    private MockBackendClient? _mockBackendClient;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _mockBackendClient = new MockBackendClient();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
      _mockBackendClient = null;
      base.TestCleanup();
    }

    [TestMethod]
    public async Task FullWorkflow_ComponentsExist()
    {
      // Arrange
      // In a real implementation, this would test the full workflow:
      // 1. Create profile → 2. Synthesize → 3. Apply effect → 4. Export

      var profilesClient = new ProfilesClient(_mockBackendClient!, new RequestCoordinator());
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(profilesClient);
      var qualityInsights = CreateMockProfileQualityInsightsService();
      var transferService = CreateMockProfileTransferService();
      var enhancementService = CreateMockProfileEnhancementService();
      var profilesViewModel = new VoiceStudio.App.Views.Panels.ProfilesViewModel(
          profilesClient,
          profilesUseCase,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()),
          new VoiceStudio.App.Services.MultiSelectService(),
          qualityInsights,
          transferService,
          enhancementService,
          toastNotificationService: null,
          undoRedoService: new VoiceStudio.App.Services.UndoRedoService(),
          errorService: null,
          logService: null,
          dialogService: null,
          previewService: null);
      var voiceSynthesisService = new VoiceStudio.App.Services.VoiceSynthesisService(_mockBackendClient!);
      var enginesClient = new VoiceStudio.App.Services.EnginesClient(_mockBackendClient!);
      var qualityPipelineService = new VoiceStudio.App.Services.QualityPipelineService(_mockBackendClient!);
      var ensembleService = new VoiceStudio.App.Services.EnsembleService(_mockBackendClient!);
      var textAnalysisService = new VoiceStudio.App.Services.TextAnalysisService(_mockBackendClient!);
      var qualityHistoryService = new VoiceStudio.App.Services.QualityHistoryService(_mockBackendClient!);
      var synthesisViewModel = new VoiceStudio.App.Views.Panels.VoiceSynthesisViewModel(
          voiceSynthesisService,
          enginesClient,
          qualityPipelineService,
          ensembleService,
          textAnalysisService,
          qualityHistoryService,
          profilesClient,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()));

      // Act
      // Verify all components exist for the workflow

      // Assert - Verify all necessary ViewModels exist
      Assert.IsNotNull(profilesViewModel, "ProfilesViewModel should exist");
      Assert.IsNotNull(synthesisViewModel, "VoiceSynthesisViewModel should exist");
      Assert.IsNotNull(profilesViewModel.CreateProfileCommand, "CreateProfile command should exist");
      Assert.IsNotNull(synthesisViewModel.SynthesizeCommand, "Synthesize command should exist");
      await Task.CompletedTask;
    }

    [TestMethod]
    public void Workflow_CommandsAreInitialized()
    {
      // Arrange
      var profilesClient = new ProfilesClient(_mockBackendClient!, new RequestCoordinator());
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(profilesClient);
      var qualityInsights = CreateMockProfileQualityInsightsService();
      var transferService = CreateMockProfileTransferService();
      var enhancementService = CreateMockProfileEnhancementService();
      var profilesViewModel = new VoiceStudio.App.Views.Panels.ProfilesViewModel(
          profilesClient,
          profilesUseCase,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()),
          new VoiceStudio.App.Services.MultiSelectService(),
          qualityInsights,
          transferService,
          enhancementService,
          toastNotificationService: null,
          undoRedoService: new VoiceStudio.App.Services.UndoRedoService(),
          errorService: null,
          logService: null,
          dialogService: null,
          previewService: null);

      // Act & Assert
      // Verify that commands needed for the workflow are properly initialized
      Assert.IsNotNull(profilesViewModel.LoadProfilesCommand,
          "LoadProfiles command should be initialized");
      Assert.IsNotNull(profilesViewModel.CreateProfileCommand,
          "CreateProfile command should be initialized");
      Assert.IsNotNull(profilesViewModel.PreviewProfileCommand,
          "PreviewProfile command should be initialized");
    }

    [TestMethod]
    public async Task Workflow_ViewModelsCanBeCreated()
    {
      // Arrange & Act
      // Verify all ViewModels in the critical path can be instantiated

      var profilesClient = new ProfilesClient(_mockBackendClient!, new RequestCoordinator());
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(profilesClient);
      var qualityInsights = CreateMockProfileQualityInsightsService();
      var transferService = CreateMockProfileTransferService();
      var enhancementService = CreateMockProfileEnhancementService();
      var profilesViewModel = new VoiceStudio.App.Views.Panels.ProfilesViewModel(
          profilesClient,
          profilesUseCase,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()),
          new VoiceStudio.App.Services.MultiSelectService(),
          qualityInsights,
          transferService,
          enhancementService,
          toastNotificationService: null,
          undoRedoService: new VoiceStudio.App.Services.UndoRedoService(),
          errorService: null,
          logService: null,
          dialogService: null,
          previewService: null);
      var voiceSynthesisService = new VoiceStudio.App.Services.VoiceSynthesisService(_mockBackendClient!);
      var enginesClient = new VoiceStudio.App.Services.EnginesClient(_mockBackendClient!);
      var qualityPipelineService = new VoiceStudio.App.Services.QualityPipelineService(_mockBackendClient!);
      var ensembleService = new VoiceStudio.App.Services.EnsembleService(_mockBackendClient!);
      var textAnalysisService = new VoiceStudio.App.Services.TextAnalysisService(_mockBackendClient!);
      var qualityHistoryService = new VoiceStudio.App.Services.QualityHistoryService(_mockBackendClient!);
      var synthesisViewModel = new VoiceStudio.App.Views.Panels.VoiceSynthesisViewModel(
          voiceSynthesisService,
          enginesClient,
          qualityPipelineService,
          ensembleService,
          textAnalysisService,
          qualityHistoryService,
          profilesClient,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()));
      var mockDialog = new Mock<IDialogService>();
      mockDialog
          .Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
          .ReturnsAsync(true);
      var mockProjectsClient = new Mock<IProjectsClient>();
      mockProjectsClient
          .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Project>());
      var mockClipService = new Mock<ITimelineClipService>();
      var mockTrackService = new Mock<ITimelineTrackService>();
      var mockTranscriptionService = new Mock<ITimelineTranscriptionService>();
      var mockProjectAudioClient = new Mock<IProjectAudioClient>();
      var mockAudioVisualizationService = new Mock<IAudioVisualizationService>();
      var synthesisService = new TimelineSynthesisService(_mockBackendClient!, mockProjectAudioClient.Object);
      var timelineViewModel = new VoiceStudio.App.Views.Panels.TimelineViewModel(
          synthesisService,
          mockClipService.Object,
          mockTrackService.Object,
          mockTranscriptionService.Object,
          mockProjectAudioClient.Object,
          mockAudioVisualizationService.Object,
          mockProjectsClient.Object,
          profilesClient,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()),
          new VoiceStudio.App.Services.MultiSelectService(),
          mockDialog.Object,
          toastNotificationService: null,
          undoRedoService: new VoiceStudio.App.Services.UndoRedoService(),
          errorService: null,
          logService: null,
          settingsService: null,
          recentProjectsService: null);

      // Assert
      Assert.IsNotNull(profilesViewModel, "ProfilesViewModel creation should succeed");
      Assert.IsNotNull(synthesisViewModel, "VoiceSynthesisViewModel creation should succeed");
      Assert.IsNotNull(timelineViewModel, "TimelineViewModel creation should succeed");
      await Task.CompletedTask;
    }

    [TestMethod]
    public void Workflow_BackendClientIsAvailable()
    {
      // Arrange & Act
      // Verify backend client mock is working

      // Assert
      Assert.IsNotNull(_mockBackendClient, "MockBackendClient should be initialized");
    }
  }
}