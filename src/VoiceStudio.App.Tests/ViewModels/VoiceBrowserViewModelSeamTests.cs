using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for VoiceBrowserViewModel.
  /// Instantiates ViewModel with mocked IVoiceBrowserClient.
  /// Supports "VoiceBrowserViewModel migrated to IVoiceBrowserClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class VoiceBrowserViewModelSeamTests : ViewModelTestBase
  {
    private Mock<IVoiceBrowserClient> _mockClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _mockClient = new Mock<IVoiceBrowserClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new VoiceBrowserViewModel(MockContext!, _mockClient.Object, _mockAudioPlayer.Object);
      _mockClient.Verify(x => x.SearchVoicesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double>(), It.IsAny<string[]?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.GetLanguagesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.GetTagsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new VoiceBrowserViewModel(MockContext!, _mockClient.Object, _mockAudioPlayer.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("voice-browser", vm.PanelId);
      Assert.IsNotNull(vm.SearchCommand);
      Assert.IsNotNull(vm.LoadLanguagesCommand);
      Assert.IsNotNull(vm.LoadTagsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new VoiceBrowserViewModel(MockContext!, null!, _mockAudioPlayer.Object);
    }
  }
}
