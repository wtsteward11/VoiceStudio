using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using CoreSettingsData = VoiceStudio.Core.Models.SettingsData;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for SettingsViewModel.
  /// Instantiates ViewModel with mocked ISettingsService, ISettingsClient.
  /// Supports "SettingsViewModel migrated to ISettingsClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class SettingsViewModelSeamTests
  {
    private Mock<ISettingsService> _mockSettingsService = null!;
    private Mock<ISettingsClient> _mockSettingsClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockSettingsService = new Mock<ISettingsService>();
      _mockSettingsClient = new Mock<ISettingsClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockSettingsClient
        .Setup(x => x.CheckDependenciesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<string, object>());
      _mockSettingsClient
        .Setup(x => x.GetEffectiveEnginePriorityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EffectiveEnginePriorityResponse
        {
          Source = "default",
          Order = new List<string> { "xtts_v2", "openvoice", "piper", "espeak" }
        });
      _mockSettingsClient
        .Setup(x => x.GetTorchVenvStatusAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((TorchVenvStatusResponse?)null);
    }

    [TestCleanup]
    public void Cleanup()
    {
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object);
      _mockSettingsClient.Verify(x => x.CheckDependenciesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Settings, vm.PanelId);
      Assert.IsNotNull(vm.LoadSettingsCommand);
      Assert.IsNotNull(vm.SaveSettingsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSettingsService_Throws()
    {
      _ = new SettingsViewModel(_context, null!, _mockSettingsClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSettingsClient_Throws()
    {
      _ = new SettingsViewModel(_context, _mockSettingsService.Object, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task ApplyBackupRestoredAsync_WhenRestoreSettingsFalse_DoesNotLoadSettings()
    {
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CoreSettingsData());

      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object);
      await vm.ApplyBackupRestoredAsync(
          new BackupRestoredEvent(PanelIds.BackupRestore, false, false, false, false),
          CancellationToken.None);

      _mockSettingsService.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task ApplyBackupRestoredAsync_WhenRestoreSettingsTrue_CallsLoadSettingsAsync()
    {
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CoreSettingsData());

      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object);
      await vm.ApplyBackupRestoredAsync(
          new BackupRestoredEvent(PanelIds.BackupRestore, false, false, restoreSettings: true, false),
          CancellationToken.None);

      _mockSettingsService.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task ApplyBackupRestoredAsync_WhenRestoreSettingsTrueAndLoadFails_SetsErrorMessage()
    {
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("disk read failed"));

      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object);
      await vm.ApplyBackupRestoredAsync(
          new BackupRestoredEvent(PanelIds.BackupRestore, false, false, true, false),
          CancellationToken.None);

      Assert.IsFalse(string.IsNullOrEmpty(vm.ErrorMessage), "Expected LoadSettings failure to surface on the ViewModel.");
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task BackupRestoredEvent_AfterOnActivated_WithRestoreSettings_PublishesLoadOnce()
    {
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CoreSettingsData());

      var agg = new EventAggregator();
      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object, null, null, agg);
      await vm.OnActivatedAsync();

      await agg.PublishAsync(new BackupRestoredEvent(PanelIds.BackupRestore, false, false, true, false));

      _mockSettingsService.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task BackupRestoredEvent_AfterOnDeactivated_DoesNotLoadOnPublish()
    {
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CoreSettingsData());

      var agg = new EventAggregator();
      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object, null, null, agg);
      await vm.OnActivatedAsync();
      await agg.PublishAsync(new BackupRestoredEvent(PanelIds.BackupRestore, false, false, true, false));
      _mockSettingsService.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);

      await vm.OnDeactivatedAsync();
      await agg.PublishAsync(new BackupRestoredEvent(PanelIds.BackupRestore, false, false, true, false));

      _mockSettingsService.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task BackupRestoredEvent_ReactivatedAfterDeactivated_LoadsAgainOnPublish()
    {
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CoreSettingsData());

      var agg = new EventAggregator();
      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object, null, null, agg);
      await vm.OnActivatedAsync();
      await agg.PublishAsync(new BackupRestoredEvent(PanelIds.BackupRestore, false, false, true, false));
      await vm.OnDeactivatedAsync();
      await vm.OnActivatedAsync();
      await agg.PublishAsync(new BackupRestoredEvent(PanelIds.BackupRestore, false, false, true, false));

      _mockSettingsService.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task OnActivated_TwiceWithoutDeactivate_SinglePublishLoadsOnce()
    {
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CoreSettingsData());

      var agg = new EventAggregator();
      var vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object, null, null, agg);
      await vm.OnActivatedAsync();
      await vm.OnActivatedAsync();
      await agg.PublishAsync(new BackupRestoredEvent(PanelIds.BackupRestore, false, false, true, false));

      _mockSettingsService.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task RestoreBackupCommand_WithSharedAggregator_ReachesActivatedSettingsViewModel()
    {
      var agg = new EventAggregator();
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CoreSettingsData());

      var settingsVm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object, null, null, agg);
      await settingsVm.OnActivatedAsync();

      var mockBackupClient = new Mock<IBackupRestoreClient>();
      mockBackupClient
          .Setup(x => x.RestoreBackupAsync(
              It.IsAny<string>(),
              It.IsAny<RestoreRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new RestoreResponse { Success = true, Message = "ok" });

      var backupVm = new BackupRestoreViewModel(_context, mockBackupClient.Object, agg)
      {
        RestoreProjects = false,
        RestoreProfiles = false,
        RestoreSettings = true,
        RestoreModels = false
      };

      var item = new BackupRestoreViewModel.BackupItem(new BackupInfo
      {
        Id = "backup-seam",
        Name = "SeamBackup",
        Created = "2026-01-01T00:00:00Z",
        SizeBytes = 1
      });

      await backupVm.RestoreBackupCommand.ExecuteAsync(item);

      _mockSettingsService.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
  }
}
