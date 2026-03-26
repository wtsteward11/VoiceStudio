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
  }
}
