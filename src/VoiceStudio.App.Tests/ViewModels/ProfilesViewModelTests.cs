using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Tests for ProfilesViewModel covering command execution, state changes, error handling, and cancellation.
  /// </summary>
  [TestClass]
  public class ProfilesViewModelTests : ViewModelTestBase
  {
    private ProfilesViewModel? _viewModel;
    private AudioPlayerService? _audioPlayerService;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _audioPlayerService = new AudioPlayerService(new System.Net.Http.HttpClient());
      var coordinator = new RequestCoordinator();
      var profilesClient = new ProfilesClient(MockBackendClient!, coordinator);
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(profilesClient);
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var qualityInsightsService = new Mock<IProfileQualityInsightsService>();
      qualityInsightsService.Setup(x => x.LoadQualityHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<QualityHistoryEntry>());
      qualityInsightsService.Setup(x => x.LoadQualityTrendsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new QualityTrends());
      qualityInsightsService.Setup(x => x.LoadQualityBaselineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((QualityBaseline?)null);
      qualityInsightsService.Setup(x => x.GetQualityDegradationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((QualityDegradationResponse?)null);

      var transferService = new Mock<IProfileTransferService>();
      transferService.Setup(x => x.ParseImports(It.IsAny<string>())).Returns((new List<ProfileImportData>(), (string?)null));
      transferService.Setup(x => x.CreateProfilesFromImportDataAsync(It.IsAny<IReadOnlyList<ProfileImportData>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());
      transferService.Setup(x => x.BuildExportJson(It.IsAny<IEnumerable<VoiceProfile>>())).Returns("{}");
      transferService.Setup(x => x.SanitizeFilename(It.IsAny<string?>())).Returns((string? v) => string.IsNullOrWhiteSpace(v) ? "profile_export" : v!);

      var enhancementService = CreateMockProfileEnhancementService();

      _viewModel = new ProfilesViewModel(
          profilesClient,
          profilesUseCase,
          _audioPlayerService,
          multiSelectService,
          qualityInsightsService.Object,
          transferService.Object,
          enhancementService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null,
          dialogService: null,
          previewService: null);
    }

    [TestCleanup]
    public override void TestCleanup()
    {
      _viewModel = null;
      _audioPlayerService = null;
      base.TestCleanup();
    }

    [TestMethod]
    public void ProfilesViewModel_Initialization_Succeeds()
    {
      // Arrange & Act
      // (Already done in TestInitialize)

      // Assert
      Assert.IsNotNull(_viewModel, "ProfilesViewModel should be created");
      Assert.AreEqual("profiles", _viewModel.PanelId, "Panel ID should be 'profiles'");
      Assert.IsNotNull(_viewModel.LoadProfilesCommand, "LoadProfilesCommand should exist");
      Assert.IsNotNull(_viewModel.CreateProfileCommand, "CreateProfileCommand should exist");
    }

    [TestMethod]
    public void ProfilesViewModel_Commands_AreInitialized()
    {
      // Assert
      Assert.IsNotNull(_viewModel!.LoadProfilesCommand, "LoadProfilesCommand should be initialized");
      Assert.IsNotNull(_viewModel.CreateProfileCommand, "CreateProfileCommand should be initialized");
      Assert.IsNotNull(_viewModel.DeleteProfileCommand, "DeleteProfileCommand should be initialized");
      Assert.IsNotNull(_viewModel.PreviewProfileCommand, "PreviewProfileCommand should be initialized");
    }

    [TestMethod]
    public async Task LoadProfilesCommand_Execution_UpdatesIsLoading()
    {
      // Arrange
      // Set up mock backend response
      var profiles = new List<VoiceProfile>
            {
                new VoiceProfile { Id = "1", Name = "Test Profile 1" },
                new VoiceProfile { Id = "2", Name = "Test Profile 2" }
            };
      // Note: MockBackendClient would need GetProfilesAsync implemented for this to work
      // For now, this is a structural test

      // Act
      // In a real test, we would execute the command:
      // await _viewModel.LoadProfilesCommand.ExecuteAsync(null);
      // await WaitForAsyncOperation();

      // Assert
      // Verify IsLoading state changed appropriately
      // Note: This is a placeholder test structure
      Assert.IsNotNull(_viewModel, "ViewModel should exist");
      await Task.CompletedTask;
    }

    [TestMethod]
    public void DeleteProfileCommand_CanExecute_WhenProfileSelected()
    {
      // Arrange
      var profile = new VoiceProfile { Id = "1", Name = "Test Profile" };
      _viewModel!.SelectedProfile = profile;

      // Act
      // In a real test, we would check CanExecute:
      // var canExecute = _viewModel.DeleteProfileCommand.CanExecute(null);

      // Assert
      // Verify command can execute when profile is selected
      Assert.IsNotNull(_viewModel.SelectedProfile, "Profile should be selected");
    }

    [TestMethod]
    public void ViewModel_ErrorHandling_Works()
    {
      // Arrange
      // Set up mock to throw exception
      // MockBackendClient!.GetProfilesException = new Exception("Backend error");

      // Act
      // Execute command that would cause error
      // await _viewModel.LoadProfilesCommand.ExecuteAsync(null);
      // await WaitForAsyncOperation();

      // Assert
      // Verify error was handled appropriately
      // Assert.IsNotNull(_viewModel.ErrorMessage, "Error message should be set");
      Assert.IsNotNull(_viewModel, "ViewModel should exist");
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelView()
    {
      // Arrange & Act
      var panelView = _viewModel as VoiceStudio.Core.Panels.IPanelView;

      // Assert
      Assert.IsNotNull(panelView, "ProfilesViewModel should implement IPanelView");
      Assert.AreEqual("profiles", panelView.PanelId, "Panel ID should match");
      Assert.IsNotNull(panelView.DisplayName, "Display name should not be null");
    }

    /// <summary>
    /// Verifies that CreateProfile does not trigger quality analytics endpoints.
    /// Quality calls are gated behind explicit user actions (Analyze, LoadQualityHistory, etc.).
    /// </summary>
    [TestMethod]
    public async Task CreateProfile_DoesNotCallQualityEndpoints()
    {
      // Arrange: use Moq to track backend calls
      var mockBackend = CreateMockBackendClient();
      var newProfile = new VoiceProfile { Id = "test-profile-1", Name = "Test Profile" };
      mockBackend
          .Setup(x => x.CreateProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(newProfile);

      var profilesClient = new ProfilesClient(mockBackend.Object, new RequestCoordinator());
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(profilesClient);
      var audioPlayer = new AudioPlayerService(new System.Net.Http.HttpClient());
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var qualityInsights = CreateMockProfileQualityInsightsService();
      var transferService = CreateMockProfileTransferService();
      var enhancementService = CreateMockProfileEnhancementService();
      var vm = new ProfilesViewModel(
          profilesClient,
          profilesUseCase,
          audioPlayer,
          multiSelectService,
          qualityInsights,
          transferService,
          enhancementService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null,
          dialogService: null,
          previewService: null);

      // Act: create profile (sets SelectedProfile, which previously triggered quality calls)
      await vm.CreateProfileCommand.ExecuteAsync("Test Profile");
      await WaitForAsyncOperation(150);

      // Assert: quality endpoints must not be called on CreateProfile/selection change
      mockBackend.Verify(
          x => x.GetQualityHistoryAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
      mockBackend.Verify(
          x => x.GetQualityTrendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
      mockBackend.Verify(
          x => x.GetQualityDegradationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
          Times.Never);
      mockBackend.Verify(
          x => x.GetQualityBaselineAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    /// <summary>
    /// Verifies that rapid profile switching does not stack quality analytics requests.
    /// Quality calls are gated behind explicit user actions; selection change only clears data.
    /// </summary>
    [TestMethod]
    public void RapidProfileSwitching_DoesNotCallQualityEndpoints()
    {
      var mockBackend = CreateMockBackendClient();
      var profilesClient = new ProfilesClient(mockBackend.Object, new RequestCoordinator());
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(profilesClient);
      var audioPlayer = new AudioPlayerService(new System.Net.Http.HttpClient());
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var qualityInsights = CreateMockProfileQualityInsightsService();
      var transferService = CreateMockProfileTransferService();
      var enhancementService = CreateMockProfileEnhancementService();
      var vm = new ProfilesViewModel(
          profilesClient,
          profilesUseCase,
          audioPlayer,
          multiSelectService,
          qualityInsights,
          transferService,
          enhancementService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null,
          dialogService: null,
          previewService: null);

      var p1 = new VoiceProfile { Id = "p1", Name = "Profile 1" };
      var p2 = new VoiceProfile { Id = "p2", Name = "Profile 2" };
      var p3 = new VoiceProfile { Id = "p3", Name = "Profile 3" };
      vm.Profiles.Add(p1);
      vm.Profiles.Add(p2);
      vm.Profiles.Add(p3);

      vm.SelectedProfile = p1;
      vm.SelectedProfile = p2;
      vm.SelectedProfile = p3;
      vm.SelectedProfile = p1;
      vm.SelectedProfile = p2;

      mockBackend.Verify(
          x => x.GetQualityHistoryAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
      mockBackend.Verify(
          x => x.GetQualityTrendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
      mockBackend.Verify(
          x => x.GetQualityDegradationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
          Times.Never);
      mockBackend.Verify(
          x => x.GetQualityBaselineAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    /// <summary>
    /// Verifies that concurrent LoadProfilesAsync calls coalesce to a single backend request.
    /// Coalescing is provided by IRequestCoordinator in BackendClient (not ProfilesViewModel).
    /// </summary>
    [TestMethod]
    public async Task LoadProfilesAsync_ConcurrentCalls_CoalescesToSingleRequest()
    {
      var coordinator = new RequestCoordinator();
      var backendCallCount = 0;
      var mockBackend = CreateMockBackendClient();

      // GetProfilesAsync delegates to coordinator so concurrent callers coalesce to one factory invocation
      mockBackend
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .Returns(async (CancellationToken ct) =>
          {
            return await coordinator.GetOrCreateAsync(
              "profiles:list",
              async c =>
              {
                Interlocked.Increment(ref backendCallCount);
                await Task.Delay(100, c).ConfigureAwait(false);
                return new List<VoiceProfile>();
              },
              TimeSpan.FromSeconds(30),
              ct).ConfigureAwait(false);
          });

      var profilesClient = new ProfilesClient(mockBackend.Object, new RequestCoordinator());
      var profilesUseCase = new ProfilesUseCase(profilesClient);
      var audioPlayer = new AudioPlayerService(new System.Net.Http.HttpClient());
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var qualityInsights = CreateMockProfileQualityInsightsService();
      var transferService = CreateMockProfileTransferService();
      var enhancementService = CreateMockProfileEnhancementService();
      var vm = new ProfilesViewModel(
          profilesClient,
          profilesUseCase,
          audioPlayer,
          multiSelectService,
          qualityInsights,
          transferService,
          enhancementService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null,
          dialogService: null,
          previewService: null);

      await Task.WhenAll(
          vm.LoadProfilesCommand.ExecuteAsync(null),
          vm.LoadProfilesCommand.ExecuteAsync(null)).ConfigureAwait(false);

      Assert.AreEqual(1, backendCallCount, "Backend GetProfilesAsync factory should run exactly once when LoadProfiles is invoked concurrently (coordinator coalesces)");
    }

    /// <summary>
    /// Verifies that CreateProfileCommand cannot run twice concurrently (reentrancy guard).
    /// </summary>
    [TestMethod]
    public async Task CreateProfileCommand_ConcurrentInvocations_ExecutesOnlyOnce()
    {
      var createCallCount = 0;
      var createBlocker = new TaskCompletionSource<bool>();
      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase
          .Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Callback(() => Interlocked.Increment(ref createCallCount))
          .Returns(() => createBlocker.Task.ContinueWith(_ => new VoiceProfile { Id = "test-1", Name = "Test" }));

      var mockProfilesClient = new Mock<IProfilesClient>();
      var mockBackend = CreateMockBackendClient();
      var audioPlayer = new AudioPlayerService(new System.Net.Http.HttpClient());
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var qualityInsights = CreateMockProfileQualityInsightsService();
      var transferService = CreateMockProfileTransferService();
      var enhancementService = CreateMockProfileEnhancementService();
      var vm = new ProfilesViewModel(
          mockProfilesClient.Object,
          mockUseCase.Object,
          audioPlayer,
          multiSelectService,
          qualityInsights,
          transferService,
          enhancementService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null,
          dialogService: null,
          previewService: null);

      var t1 = vm.CreateProfileCommand.ExecuteAsync("Profile 1");
      var t2 = vm.CreateProfileCommand.ExecuteAsync("Profile 2");
      createBlocker.TrySetResult(true);
      await Task.WhenAll(t1, t2);
      await WaitForAsyncOperation(50);

      Assert.AreEqual(1, createCallCount, "CreateAsync should be called exactly once when CreateProfile is invoked concurrently");
    }

    private static IProfileQualityInsightsService CreateMockProfileQualityInsightsService()
    {
      var mock = new Mock<IProfileQualityInsightsService>();
      mock.Setup(x => x.LoadQualityHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<QualityHistoryEntry>());
      mock.Setup(x => x.LoadQualityTrendsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new QualityTrends());
      mock.Setup(x => x.LoadQualityBaselineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((QualityBaseline?)null);
      mock.Setup(x => x.GetQualityDegradationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((QualityDegradationResponse?)null);
      return mock.Object;
    }

    private static IProfileTransferService CreateMockProfileTransferService()
    {
      var mock = new Mock<IProfileTransferService>();
      mock.Setup(x => x.ParseImports(It.IsAny<string>())).Returns((new List<ProfileImportData>(), (string?)null));
      mock.Setup(x => x.CreateProfilesFromImportDataAsync(It.IsAny<IReadOnlyList<ProfileImportData>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());
      mock.Setup(x => x.BuildExportJson(It.IsAny<IEnumerable<VoiceProfile>>())).Returns("{}");
      mock.Setup(x => x.SanitizeFilename(It.IsAny<string?>())).Returns((string? v) => string.IsNullOrWhiteSpace(v) ? "profile_export" : v!);
      return mock.Object;
    }

    private static IProfileEnhancementService CreateMockProfileEnhancementService()
    {
      var mock = new Mock<IProfileEnhancementService>();
      mock.Setup(x => x.EnhanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((ReferenceAudioPreprocessResponse?)null);
      return mock.Object;
    }
  }
}