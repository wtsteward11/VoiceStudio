using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for AdvancedRealTimeVisualizationViewModel.
  /// Instantiates ViewModel with mocked IAdvancedRealTimeVisualizationClient.
  /// Supports "AdvancedRealTimeVisualizationViewModel migrated to IAdvancedRealTimeVisualizationClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// Note: ViewModel starts a timer in constructor that invokes client; Constructor_DoesNotCallClient omitted (timer-based FAF).
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AdvancedRealTimeVisualizationViewModelSeamTests
  {
    private Mock<IAdvancedRealTimeVisualizationClient> _mockClient = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IAdvancedRealTimeVisualizationClient>();
      _mockClient
          .Setup(x => x.GetVisualizationDataAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<System.Threading.CancellationToken>()))
          .ReturnsAsync(new System.Collections.Generic.Dictionary<string, object>());
      _mockClient
          .Setup(x => x.GetPlaybackPositionAsync(It.IsAny<System.Threading.CancellationToken>()))
          .ReturnsAsync(TimeSpan.Zero);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new AdvancedRealTimeVisualizationViewModel(_mockClient.Object);

      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.SavePresetCommand);
      Assert.IsNotNull(vm.ResetViewCommand);
      Assert.IsFalse(string.IsNullOrEmpty(vm.VisualizationType));
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new AdvancedRealTimeVisualizationViewModel(null!);
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
      var vm = new AdvancedRealTimeVisualizationViewModel(_mockClient.Object);
      vm.Dispose();
    }
  }
}
