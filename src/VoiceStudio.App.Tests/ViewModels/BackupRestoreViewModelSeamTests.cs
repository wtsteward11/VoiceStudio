using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for BackupRestoreViewModel.
  /// Instantiates ViewModel with mocked IBackupRestoreClient.
  /// Supports "BackupRestoreViewModel migrated to IBackupRestoreClient" claims.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class BackupRestoreViewModelSeamTests
  {
    private Mock<IBackupRestoreClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IBackupRestoreClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
          .Setup(x => x.GetBackupsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<BackupInfo>());
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new BackupRestoreViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.GetBackupsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIBackupRestoreClient_CreatesInstance()
    {
      var vm = new BackupRestoreViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.BackupRestore, vm.PanelId);
      Assert.IsNotNull(vm.LoadBackupsCommand);
      Assert.IsNotNull(vm.CreateBackupCommand);
      Assert.IsNotNull(vm.RestoreBackupCommand);
      Assert.IsNotNull(vm.DeleteBackupCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new BackupRestoreViewModel(_context, null!);
    }

    [TestMethod]
    public async Task LoadBackupsCommand_CallsIBackupRestoreClient_GetBackupsAsync()
    {
      var vm = new BackupRestoreViewModel(_context, _mockClient.Object);
      await vm.LoadBackupsCommand.ExecuteAsync(null);
      _mockClient.Verify(
          x => x.GetBackupsAsync(It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Pass 06 C4: successful restore publishes <see cref="BackupRestoredEvent"/> with restore checkbox flags.
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task RestoreBackupCommand_OnSuccess_PublishesBackupRestoredEvent_WithCheckboxFlags()
    {
      var mockAgg = new Mock<IEventAggregator>();
      mockAgg
          .Setup(x => x.PublishAsync(It.IsAny<BackupRestoredEvent>()))
          .Returns(Task.CompletedTask);
      _mockClient
          .Setup(x => x.RestoreBackupAsync(
              It.IsAny<string>(),
              It.IsAny<RestoreRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new RestoreResponse { Success = true, Message = "ok" });

      var vm = new BackupRestoreViewModel(_context, _mockClient.Object, mockAgg.Object)
      {
        RestoreProjects = true,
        RestoreProfiles = false,
        RestoreSettings = true,
        RestoreModels = false
      };

      var item = new BackupRestoreViewModel.BackupItem(new BackupInfo
      {
        Id = "backup-1",
        Name = "MyBackup",
        Created = "2026-01-01T00:00:00Z",
        SizeBytes = 100
      });

      await vm.RestoreBackupCommand.ExecuteAsync(item);

      mockAgg.Verify(
          x => x.PublishAsync(
              It.Is<BackupRestoredEvent>(e =>
                  e.SourcePanelId == PanelIds.BackupRestore
                  && e.RestoreProjects
                  && !e.RestoreProfiles
                  && e.RestoreSettings
                  && !e.RestoreModels)),
          Times.Once);

      _mockClient.Verify(
          x => x.RestoreBackupAsync(
              "backup-1",
              It.Is<RestoreRequest>(r =>
                  r.BackupId == "backup-1"
                  && r.RestoreProjects
                  && !r.RestoreProfiles
                  && r.RestoreSettings
                  && !r.RestoreModels),
              It.IsAny<CancellationToken>()),
          Times.Once);

      _mockClient.Verify(
          x => x.CreateBackupAsync(It.IsAny<BackupCreateRequest>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _mockClient.Verify(
          x => x.UploadBackupAsync(It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    /// <summary>
    /// Pass 06 C4: when post-restore publish fails, surface partial-refresh status (disk restored, session not fully refreshed).
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task RestoreBackupCommand_WhenPublishFails_SetsPartialRefreshStatusMessage()
    {
      var mockAgg = new Mock<IEventAggregator>();
      mockAgg
          .Setup(x => x.PublishAsync(It.IsAny<BackupRestoredEvent>()))
          .ThrowsAsync(new InvalidOperationException("event bus"));
      _mockClient
          .Setup(x => x.RestoreBackupAsync(
              It.IsAny<string>(),
              It.IsAny<RestoreRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new RestoreResponse { Success = true });

      var vm = new BackupRestoreViewModel(_context, _mockClient.Object, mockAgg.Object);
      var item = new BackupRestoreViewModel.BackupItem(new BackupInfo
      {
        Id = "b2",
        Name = "PartialBackup",
        Created = "2026-01-01T00:00:00Z",
        SizeBytes = 1
      });

      await vm.RestoreBackupCommand.ExecuteAsync(item);

      Assert.IsTrue(string.IsNullOrEmpty(vm.ErrorMessage));
      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.StatusMessage));
    }

    /// <summary>
    /// Pass 06 C4: without an event aggregator, restore still completes API call and does not touch unrelated client methods.
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task RestoreBackupCommand_WithoutEventAggregator_StillCallsRestoreOnly()
    {
      _mockClient
          .Setup(x => x.RestoreBackupAsync(
              It.IsAny<string>(),
              It.IsAny<RestoreRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new RestoreResponse { Success = true });

      var vm = new BackupRestoreViewModel(_context, _mockClient.Object, eventAggregator: null);
      var item = new BackupRestoreViewModel.BackupItem(new BackupInfo
      {
        Id = "b3",
        Name = "NoAgg",
        Created = "2026-01-01T00:00:00Z",
        SizeBytes = 1
      });

      await vm.RestoreBackupCommand.ExecuteAsync(item);

      _mockClient.Verify(
          x => x.RestoreBackupAsync(
              "b3",
              It.IsAny<RestoreRequest>(),
              It.IsAny<CancellationToken>()),
          Times.Once);
      _mockClient.Verify(
          x => x.CreateBackupAsync(It.IsAny<BackupCreateRequest>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    /// <summary>
    /// Pass 06 slice 3 (D5): while restore is in flight with RestoreModels, busy detail uses long-hint path.
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task RestoreBackupCommand_WithRestoreModels_SetsLongBusyDetailWhileInFlight()
    {
      var gate = new TaskCompletionSource<bool>();
      _mockClient
          .Setup(x => x.RestoreBackupAsync(
              It.IsAny<string>(),
              It.IsAny<RestoreRequest>(),
              It.IsAny<CancellationToken>()))
          .Returns(async (string id, RestoreRequest r, CancellationToken ct) =>
          {
            await gate.Task.WaitAsync(ct);
            return new RestoreResponse { Success = true };
          });

      var vm = new BackupRestoreViewModel(_context, _mockClient.Object) { RestoreModels = true };
      var item = new BackupRestoreViewModel.BackupItem(new BackupInfo
      {
        Id = "m1",
        Name = "ModelsBackup",
        Created = "2026-01-01T00:00:00Z",
        SizeBytes = 1
      });

      var execTask = vm.RestoreBackupCommand.ExecuteAsync(item);
      await Task.Delay(80);
      Assert.IsTrue(vm.IsRestoring);
      Assert.IsFalse(string.IsNullOrEmpty(vm.RestoreBusyDetail));
      StringAssert.Contains(vm.RestoreBusyDetail, "minute", System.StringComparison.OrdinalIgnoreCase);
      gate.TrySetResult(true);
      await execTask;
      Assert.IsFalse(vm.IsRestoring);
    }

    /// <summary>
    /// Pass 06 slice 3 (D5): user cancel does not publish <see cref="BackupRestoredEvent"/> and clears restoring.
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task RestoreBackupCommand_WhenCanceled_DoesNotPublish_AndClearsRestoring()
    {
      var mockAgg = new Mock<IEventAggregator>();
      _mockClient
          .Setup(x => x.RestoreBackupAsync(
              It.IsAny<string>(),
              It.IsAny<RestoreRequest>(),
              It.IsAny<CancellationToken>()))
          .Returns<string, RestoreRequest, CancellationToken>(async (_, _, ct) =>
          {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new RestoreResponse { Success = true };
          });

      var vm = new BackupRestoreViewModel(_context, _mockClient.Object, mockAgg.Object);
      var item = new BackupRestoreViewModel.BackupItem(new BackupInfo
      {
        Id = "c1",
        Name = "CancelMe",
        Created = "2026-01-01T00:00:00Z",
        SizeBytes = 1
      });

      var execTask = vm.RestoreBackupCommand.ExecuteAsync(item);
      await Task.Delay(100);
      Assert.IsTrue(vm.IsRestoring);
      vm.CancelRestoreCommand.Execute(null);
      await execTask;

      Assert.IsFalse(vm.IsRestoring);
      mockAgg.Verify(x => x.PublishAsync(It.IsAny<BackupRestoredEvent>()), Times.Never);
      Assert.IsFalse(string.IsNullOrEmpty(vm.StatusMessage));
      StringAssert.Contains(vm.StatusMessage, "cancel", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pass 06 slice 3 (D5): success copy when only models restored references models-specific messaging.
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task RestoreBackupCommand_WhenRestoreModelsOnly_UsesModelsSessionMessage()
    {
      _mockClient
          .Setup(x => x.RestoreBackupAsync(
              It.IsAny<string>(),
              It.IsAny<RestoreRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new RestoreResponse { Success = true });

      var mockAgg = new Mock<IEventAggregator>();
      mockAgg.Setup(x => x.PublishAsync(It.IsAny<BackupRestoredEvent>())).Returns(Task.CompletedTask);

      var vm = new BackupRestoreViewModel(_context, _mockClient.Object, mockAgg.Object)
      {
        RestoreProjects = false,
        RestoreProfiles = false,
        RestoreSettings = false,
        RestoreModels = true
      };
      var item = new BackupRestoreViewModel.BackupItem(new BackupInfo
      {
        Id = "mod1",
        Name = "MOnly",
        Created = "2026-01-01T00:00:00Z",
        SizeBytes = 1
      });

      await vm.RestoreBackupCommand.ExecuteAsync(item);

      Assert.IsFalse(string.IsNullOrEmpty(vm.StatusMessage));
      StringAssert.Contains(vm.StatusMessage, "model", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pass 06 slice 4 (D4): merge-expectation hint is non-empty and describes merge/overwrite (not a full wipe).
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public void RestoreMergeExpectationHint_DescribesMergeAndOverwrite()
    {
      var vm = new BackupRestoreViewModel(_context, _mockClient.Object);
      var hint = vm.RestoreMergeExpectationHint;
      Assert.IsFalse(string.IsNullOrWhiteSpace(hint));
      StringAssert.Contains(hint, "merge", System.StringComparison.OrdinalIgnoreCase);
      StringAssert.Contains(hint, "overwrite", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pass 06 slice 4 (D4): hint must not read like a full-folder replacement; negation of complete wipe is explicit.
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public void RestoreMergeExpectationHint_ExplicitlyDeniesCompleteWipeNarrative()
    {
      var vm = new BackupRestoreViewModel(_context, _mockClient.Object);
      var hint = vm.RestoreMergeExpectationHint;
      StringAssert.Contains(hint, "not", System.StringComparison.OrdinalIgnoreCase);
      StringAssert.Contains(hint, "complete wipe", System.StringComparison.OrdinalIgnoreCase);
    }
  }
}
