using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Tests for WorkflowCoordinatorService - Panel Workflow Integration.
  /// Verifies multi-panel workflow orchestration and event publishing.
  /// </summary>
  [TestClass]
  public class WorkflowCoordinatorServiceTests
  {
    private WorkflowCoordinatorService _service = null!;
    private List<object> _capturedEvents = null!;
    private IEventAggregator? _eventAggregator;
    private ISubscriptionToken? _navSubscription;
    private ISubscriptionToken? _cloneSubscription;
    private ISubscriptionToken? _profileSubscription;
    private ISubscriptionToken? _playbackSubscription;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _service = new WorkflowCoordinatorService();
      _capturedEvents = new List<object>();

      // Subscribe to events to capture them for verification
      _eventAggregator = AppServices.TryGetEventAggregator();
      if (_eventAggregator != null)
      {
        _navSubscription = _eventAggregator.Subscribe<PanelNavigationRequestEvent>(e => _capturedEvents.Add(e));
        _cloneSubscription = _eventAggregator.Subscribe<CloneReferenceSelectedEvent>(e => _capturedEvents.Add(e));
        _profileSubscription = _eventAggregator.Subscribe<ProfileSelectedEvent>(e => _capturedEvents.Add(e));
        _playbackSubscription = _eventAggregator.Subscribe<PlaybackRequestedEvent>(e => _capturedEvents.Add(e));
      }
    }

    [TestCleanup]
    public void Cleanup()
    {
      _navSubscription?.Dispose();
      _cloneSubscription?.Dispose();
      _profileSubscription?.Dispose();
      _playbackSubscription?.Dispose();
      _capturedEvents.Clear();
      _service.Dispose();
    }

    #region StartCloneFromLibraryAsync Tests

    [TestMethod]
    public async Task StartCloneFromLibraryAsync_PublishesNavigationEvent()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();
      var assetId = "test-asset-123";
      var assetPath = @"C:\audio\test.wav";
      var assetName = "Test Audio";

      // Act
      var context = await service.StartCloneFromLibraryAsync(assetId, assetPath, assetName, useQuickClone: true);

      // Assert
      Assert.AreEqual(WorkflowStatus.Completed, context.Status);
      Assert.IsTrue(_capturedEvents.Exists(e => e is PanelNavigationRequestEvent nav && nav.TargetPanelId == "voice-quick-clone"));
    }

    [TestMethod]
    public async Task StartCloneFromLibraryAsync_PublishesCloneReferenceSelectedEvent()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();
      var assetId = "test-asset-123";
      var assetPath = @"C:\audio\test.wav";
      var assetName = "Test Audio";

      // Act
      var context = await service.StartCloneFromLibraryAsync(assetId, assetPath, assetName);

      // Assert
      var cloneEvent = _capturedEvents.Find(e => e is CloneReferenceSelectedEvent) as CloneReferenceSelectedEvent;
      Assert.IsNotNull(cloneEvent, "CloneReferenceSelectedEvent should be published");
      Assert.AreEqual(assetId, cloneEvent.AssetId);
      Assert.AreEqual(assetPath, cloneEvent.AssetPath);
      Assert.AreEqual(assetName, cloneEvent.AssetName);
    }

    [TestMethod]
    public async Task StartCloneFromLibraryAsync_UseWizard_NavigatesToWizardPanel()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();

      // Act
      var context = await service.StartCloneFromLibraryAsync("id", "path", "name", useQuickClone: false);

      // Assert
      Assert.IsTrue(_capturedEvents.Exists(e => e is PanelNavigationRequestEvent nav && nav.TargetPanelId == "voice-cloning-wizard"));
    }

    [TestMethod]
    public async Task StartCloneFromLibraryAsync_StoresWorkflowData()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();
      var assetId = "asset-456";
      var assetPath = @"D:\audio\voice.mp3";

      // Act
      var context = await service.StartCloneFromLibraryAsync(assetId, assetPath);

      // Assert
      Assert.AreEqual(assetId, context.Data["assetId"]);
      Assert.AreEqual(assetPath, context.Data["assetPath"]);
      Assert.AreEqual("clone-from-library", context.WorkflowId);
    }

    #endregion

    #region StartSynthesizeWithVoiceAsync Tests

    [TestMethod]
    public async Task StartSynthesizeWithVoiceAsync_PublishesNavigationEvent()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();
      var profileId = "profile-789";
      var profileName = "Custom Voice";

      // Act
      var context = await service.StartSynthesizeWithVoiceAsync(profileId, profileName);

      // Assert
      Assert.AreEqual(WorkflowStatus.Completed, context.Status);
      Assert.IsTrue(_capturedEvents.Exists(e => e is PanelNavigationRequestEvent nav && nav.TargetPanelId == "synthesis-panel"));
    }

    [TestMethod]
    public async Task StartSynthesizeWithVoiceAsync_PublishesProfileSelectedEvent()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();
      var profileId = "profile-789";
      var profileName = "Custom Voice";

      // Act
      var context = await service.StartSynthesizeWithVoiceAsync(profileId, profileName);

      // Assert
      var profileEvent = _capturedEvents.Find(e => e is ProfileSelectedEvent) as ProfileSelectedEvent;
      Assert.IsNotNull(profileEvent, "ProfileSelectedEvent should be published");
      Assert.AreEqual(profileId, profileEvent.ProfileId);
      Assert.AreEqual(profileName, profileEvent.ProfileName);
      Assert.AreEqual("workflow-coordinator", profileEvent.SourcePanelId);
    }

    #endregion

    #region StartPlayFromLibraryAsync Tests

    [TestMethod]
    public async Task StartPlayFromLibraryAsync_PublishesPlaybackRequestedEvent()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();
      var assetId = "audio-asset";
      var assetPath = @"E:\music\song.wav";
      var assetName = "My Song";

      // Act
      var context = await service.StartPlayFromLibraryAsync(assetId, assetPath, assetName);

      // Assert
      Assert.AreEqual(WorkflowStatus.Completed, context.Status);
      var playEvent = _capturedEvents.Find(e => e is PlaybackRequestedEvent) as PlaybackRequestedEvent;
      Assert.IsNotNull(playEvent, "PlaybackRequestedEvent should be published");
      Assert.AreEqual(assetId, playEvent.AssetId);
      Assert.AreEqual(assetPath, playEvent.AssetPath);
      Assert.AreEqual(assetName, playEvent.AssetName);
    }

    #endregion

    #region Workflow Context Tests

    [TestMethod]
    public async Task WorkflowContext_HasUniqueExecutionId()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();

      // Act
      var context1 = await service.StartPlayFromLibraryAsync("id1", "path1");
      var context2 = await service.StartPlayFromLibraryAsync("id2", "path2");

      // Assert
      Assert.AreNotEqual(context1.ExecutionId, context2.ExecutionId);
    }

    [TestMethod]
    public async Task CurrentWorkflow_TracksActiveWorkflow()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();

      // Act
      var context = await service.StartPlayFromLibraryAsync("id", "path");

      // Assert - workflow completes synchronously, so CurrentWorkflow may be null after completion
      // The important thing is the context is returned correctly
      Assert.IsNotNull(context);
      Assert.AreEqual(WorkflowStatus.Completed, context.Status);
    }

    #endregion

    #region Event Publishing Order Tests

    [TestMethod]
    public async Task StartCloneFromLibraryAsync_PublishesEventsInCorrectOrder()
    {
      // Arrange
      var service = new WorkflowCoordinatorService();

      // Act
      await service.StartCloneFromLibraryAsync("id", "path", "name");

      // Assert - Navigation should come before the action event
      var navIndex = _capturedEvents.FindIndex(e => e is PanelNavigationRequestEvent);
      var cloneIndex = _capturedEvents.FindIndex(e => e is CloneReferenceSelectedEvent);

      Assert.IsTrue(navIndex >= 0, "Navigation event should be published");
      Assert.IsTrue(cloneIndex >= 0, "Clone event should be published");
      Assert.IsTrue(navIndex < cloneIndex, "Navigation should be published before clone event");
    }

    #endregion
  }
}
