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
  /// Seam-aware tests for VoiceMorphingBlendingViewModel.
  /// Instantiates ViewModel with mocked IVoiceMorphingBlendingClient.
  /// Supports "VoiceMorphingBlendingViewModel migrated to IVoiceMorphingBlendingClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class VoiceMorphingBlendingViewModelSeamTests : ViewModelTestBase
  {
    private Mock<IVoiceMorphingBlendingClient> _mockClient = null!;
    private Mock<IProfilesClient> _mockProfiles = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _mockClient = new Mock<IVoiceMorphingBlendingClient>();
      _mockProfiles = new Mock<IProfilesClient>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new VoiceMorphingBlendingViewModel(MockContext!, _mockClient.Object, _mockProfiles.Object);
      _mockClient.Verify(x => x.PreviewBlendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new VoiceMorphingBlendingViewModel(MockContext!, _mockClient.Object, _mockProfiles.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.VoiceMorphingBlending, vm.PanelId);
      Assert.IsNotNull(vm.LoadVoiceProfilesCommand);
      Assert.IsNotNull(vm.PreviewBlendCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new VoiceMorphingBlendingViewModel(MockContext!, null!, _mockProfiles.Object);
    }
  }
}
