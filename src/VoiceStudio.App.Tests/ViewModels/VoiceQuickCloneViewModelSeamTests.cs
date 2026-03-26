using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for VoiceQuickCloneViewModel.
  /// Instantiates ViewModel with mocked IVoiceQuickCloneClient.
  /// Supports "VoiceQuickCloneViewModel migrated to IVoiceQuickCloneClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class VoiceQuickCloneViewModelSeamTests : ViewModelTestBase
  {
    private Mock<IVoiceQuickCloneClient> _mockClient = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _mockClient = new Mock<IVoiceQuickCloneClient>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new VoiceQuickCloneViewModel(MockContext!, _mockClient.Object);
      _mockClient.Verify(
          x => x.CloneVoiceAsync(It.IsAny<System.IO.Stream>(), It.IsAny<VoiceStudio.Core.Models.VoiceCloneRequest>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new VoiceQuickCloneViewModel(MockContext!, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.VoiceQuickClone, vm.PanelId);
      Assert.IsNotNull(vm.QuickCloneCommand);
      Assert.IsNotNull(vm.BrowseAudioCommand);
      Assert.IsNotNull(vm.ResetCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new VoiceQuickCloneViewModel(MockContext!, null!);
    }
  }
}
