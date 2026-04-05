using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for AudioMonitoringDashboardViewModel.
  /// Instantiates ViewModel with mocked IAudioMonitoringDashboardClient.
  /// Supports "AudioMonitoringDashboardViewModel migrated to IAudioMonitoringDashboardClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AudioMonitoringDashboardViewModelSeamTests
  {
    private Mock<IAudioMonitoringDashboardClient> _mockClient = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IAudioMonitoringDashboardClient>();
      _mockClient
          .Setup(x => x.GetAudioMetersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AudioMeters { Peak = 0.5, Rms = 0.3 });
      _mockClient
          .Setup(x => x.GetLoudnessDataAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new LoudnessData { IntegratedLufs = -23.0f });
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new AudioMonitoringDashboardViewModel(_mockClient.Object);

      _mockClient.Verify(
          x => x.GetAudioMetersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _mockClient.Verify(
          x => x.GetLoudnessDataAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new AudioMonitoringDashboardViewModel(_mockClient.Object);

      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.LoadAudioCommand);
      Assert.IsNotNull(vm.ToggleRealTimeCommand);
      Assert.IsNotNull(vm.ResetCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new AudioMonitoringDashboardViewModel(null!);
    }

    [TestMethod]
    public async Task RealtimeMeter_ProjectAndChannelMatch_AppliesLevelsFromLinearAsync()
    {
      var meter = new Mock<IMeterClient>();
      meter.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

      var ctx = new Mock<IContextManager>();
      ctx.SetupGet(c => c.ActiveProjectId).Returns("proj-1");

      var vm = new AudioMonitoringDashboardViewModel(_mockClient.Object, meter.Object, ctx.Object);
      vm.AudioId = "ch-1";
      vm.IsRealTimeEnabled = true;

      await Task.Delay(150).ConfigureAwait(false);

      meter.Raise(
          m => m.LevelsUpdated += null,
          meter.Object,
          new MeterLevelUpdate
          {
            ProjectId = "proj-1",
            ChannelId = "ch-1",
            PeakLevelLinear = 0.5,
            RmsLevelLinear = 0.3,
          });

      await Task.Delay(50).ConfigureAwait(false);

      Assert.AreEqual(20.0 * Math.Log10(0.5), vm.PeakLevel, 1e-6);
      Assert.AreEqual(20.0 * Math.Log10(0.3), vm.RmsLevel, 1e-6);

      vm.IsRealTimeEnabled = false;
      await Task.Delay(80).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RealtimeMeter_WrongChannelId_DoesNotApplyAsync()
    {
      var meter = new Mock<IMeterClient>();
      meter.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
      var ctx = new Mock<IContextManager>();
      ctx.SetupGet(c => c.ActiveProjectId).Returns("proj-1");

      var vm = new AudioMonitoringDashboardViewModel(_mockClient.Object, meter.Object, ctx.Object);
      vm.AudioId = "ch-1";
      vm.IsRealTimeEnabled = true;

      await Task.Delay(150).ConfigureAwait(false);

      var peakBefore = vm.PeakLevel;

      meter.Raise(
          m => m.LevelsUpdated += null,
          meter.Object,
          new MeterLevelUpdate
          {
            ProjectId = "proj-1",
            ChannelId = "other-channel",
            PeakLevelLinear = 0.99,
            RmsLevelLinear = 0.99,
          });

      await Task.Delay(50).ConfigureAwait(false);

      Assert.AreEqual(peakBefore, vm.PeakLevel, 1e-6);

      vm.IsRealTimeEnabled = false;
      await Task.Delay(80).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RealtimeMeter_WrongProjectId_DoesNotApplyAsync()
    {
      var meter = new Mock<IMeterClient>();
      meter.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
      var ctx = new Mock<IContextManager>();
      ctx.SetupGet(c => c.ActiveProjectId).Returns("proj-1");

      var vm = new AudioMonitoringDashboardViewModel(_mockClient.Object, meter.Object, ctx.Object);
      vm.AudioId = "ch-1";
      vm.IsRealTimeEnabled = true;

      await Task.Delay(150).ConfigureAwait(false);

      var peakBefore = vm.PeakLevel;

      meter.Raise(
          m => m.LevelsUpdated += null,
          meter.Object,
          new MeterLevelUpdate
          {
            ProjectId = "other-proj",
            ChannelId = "ch-1",
            PeakLevelLinear = 0.99,
            RmsLevelLinear = 0.99,
          });

      await Task.Delay(50).ConfigureAwait(false);

      Assert.AreEqual(peakBefore, vm.PeakLevel, 1e-6);

      vm.IsRealTimeEnabled = false;
      await Task.Delay(80).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RealtimeMeter_EmptyWireProjectId_StillAppliesWhenChannelMatchesAsync()
    {
      var meter = new Mock<IMeterClient>();
      meter.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
      var ctx = new Mock<IContextManager>();
      ctx.SetupGet(c => c.ActiveProjectId).Returns("proj-1");

      var vm = new AudioMonitoringDashboardViewModel(_mockClient.Object, meter.Object, ctx.Object);
      vm.AudioId = "ch-1";
      vm.IsRealTimeEnabled = true;

      await Task.Delay(150).ConfigureAwait(false);

      meter.Raise(
          m => m.LevelsUpdated += null,
          meter.Object,
          new MeterLevelUpdate
          {
            ProjectId = "",
            ChannelId = "ch-1",
            PeakLevelLinear = 0.25,
            RmsLevelLinear = 0.25,
          });

      await Task.Delay(50).ConfigureAwait(false);

      Assert.AreEqual(20.0 * Math.Log10(0.25), vm.PeakLevel, 1e-6);

      vm.IsRealTimeEnabled = false;
      await Task.Delay(80).ConfigureAwait(false);
    }
  }
}
