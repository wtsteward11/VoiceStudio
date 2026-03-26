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
  /// Seam-aware tests for ProsodyViewModel.
  /// Instantiates ViewModel with mocked IProsodyClient.
  /// Supports "ProsodyViewModel migrated to IProsodyClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ProsodyViewModelSeamTests : ViewModelTestBase
  {
    private Mock<IProsodyClient> _mockClient = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _mockClient = new Mock<IProsodyClient>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new ProsodyViewModel(MockContext!, _mockClient.Object);
      _mockClient.Verify(x => x.GetConfigsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new ProsodyViewModel(MockContext!, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Prosody, vm.PanelId);
      Assert.IsNotNull(vm.LoadConfigsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new ProsodyViewModel(MockContext!, null!);
    }
  }
}
