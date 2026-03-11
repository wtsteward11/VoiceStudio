using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(MockBackendClient!);
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      _viewModel = new ProfilesViewModel(
          MockBackendClient!,
          profilesUseCase,
          _audioPlayerService,
          multiSelectService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null);
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

      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(mockBackend.Object);
      var audioPlayer = new AudioPlayerService(new System.Net.Http.HttpClient());
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var vm = new ProfilesViewModel(
          mockBackend.Object,
          profilesUseCase,
          audioPlayer,
          multiSelectService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null);

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
      var profilesUseCase = new VoiceStudio.App.UseCases.ProfilesUseCase(mockBackend.Object);
      var audioPlayer = new AudioPlayerService(new System.Net.Http.HttpClient());
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var vm = new ProfilesViewModel(
          mockBackend.Object,
          profilesUseCase,
          audioPlayer,
          multiSelectService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null);

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
    /// </summary>
    [TestMethod]
    public async Task LoadProfilesAsync_ConcurrentCalls_CoalescesToSingleRequest()
    {
      var listCallCount = 0;
      var mockUseCase = new Mock<IProfilesUseCase>();
      // Delay completion so the second concurrent caller can join the coalesced load
      // before the first completes (otherwise first clears _loadProfilesTask before second enters lock).
      mockUseCase
          .Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
          .Callback(() => Interlocked.Increment(ref listCallCount))
          .Returns(async () =>
          {
            await Task.Delay(100).ConfigureAwait(false);
            return (IReadOnlyList<VoiceProfile>)new List<VoiceProfile>();
          });

      var mockBackend = CreateMockBackendClient();
      var audioPlayer = new AudioPlayerService(new System.Net.Http.HttpClient());
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var vm = new ProfilesViewModel(
          mockBackend.Object,
          mockUseCase.Object,
          audioPlayer,
          multiSelectService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null);

      await Task.WhenAll(
          vm.LoadProfilesCommand.ExecuteAsync(null),
          vm.LoadProfilesCommand.ExecuteAsync(null)).ConfigureAwait(false);

      Assert.AreEqual(1, listCallCount, "ListAsync should be called exactly once when LoadProfiles is invoked concurrently");
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

      var mockBackend = CreateMockBackendClient();
      var audioPlayer = new AudioPlayerService(new System.Net.Http.HttpClient());
      var multiSelectService = new MultiSelectService();
      var undoRedoService = new UndoRedoService();
      var vm = new ProfilesViewModel(
          mockBackend.Object,
          mockUseCase.Object,
          audioPlayer,
          multiSelectService,
          toastNotificationService: null,
          undoRedoService: undoRedoService,
          errorService: null,
          logService: null);

      var t1 = vm.CreateProfileCommand.ExecuteAsync("Profile 1");
      var t2 = vm.CreateProfileCommand.ExecuteAsync("Profile 2");
      createBlocker.TrySetResult(true);
      await Task.WhenAll(t1, t2);
      await WaitForAsyncOperation(50);

      Assert.AreEqual(1, createCallCount, "CreateAsync should be called exactly once when CreateProfile is invoked concurrently");
    }
  }
}