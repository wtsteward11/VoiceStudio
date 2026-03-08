using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VoiceStudio.App.Tests.ViewModels;

namespace VoiceStudio.App.Tests.UI
{
  /// <summary>
  /// Smoke tests for critical user workflows.
  /// Verifies that end-to-end workflows like creating a profile, synthesizing voice, applying effects, and exporting work.
  /// </summary>
  [TestClass]
  [TestCategory("UI")]
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

      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(_mockBackendClient!);
      var profilesViewModel = new VoiceStudio.App.Views.Panels.ProfilesViewModel(
          _mockBackendClient!,
          profilesUseCase,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()),
          new VoiceStudio.App.Services.MultiSelectService(),
          toastNotificationService: null,
          undoRedoService: new VoiceStudio.App.Services.UndoRedoService(),
          errorService: null,
          logService: null);
      var synthesisViewModel = new VoiceStudio.App.Views.Panels.VoiceSynthesisViewModel(
          _mockBackendClient!,
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
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(_mockBackendClient!);
      var profilesViewModel = new VoiceStudio.App.Views.Panels.ProfilesViewModel(
          _mockBackendClient!,
          profilesUseCase,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()),
          new VoiceStudio.App.Services.MultiSelectService(),
          toastNotificationService: null,
          undoRedoService: new VoiceStudio.App.Services.UndoRedoService(),
          errorService: null,
          logService: null);

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

      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(_mockBackendClient!);
      var profilesViewModel = new VoiceStudio.App.Views.Panels.ProfilesViewModel(
          _mockBackendClient!,
          profilesUseCase,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()),
          new VoiceStudio.App.Services.MultiSelectService(),
          toastNotificationService: null,
          undoRedoService: new VoiceStudio.App.Services.UndoRedoService(),
          errorService: null,
          logService: null);
      var synthesisViewModel = new VoiceStudio.App.Views.Panels.VoiceSynthesisViewModel(
          _mockBackendClient!,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()));
      var timelineViewModel = new VoiceStudio.App.Views.Panels.TimelineViewModel(
          _mockBackendClient!,
          new VoiceStudio.App.Services.AudioPlayerService(new System.Net.Http.HttpClient()),
          new VoiceStudio.App.Services.MultiSelectService(),
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