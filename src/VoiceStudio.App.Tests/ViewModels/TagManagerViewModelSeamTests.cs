using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TagManagerViewModel.
  /// Instantiates ViewModel with mocked ITagManagerClient.
  /// Supports "TagManagerViewModel migrated to ITagManagerClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TagManagerViewModelSeamTests : ViewModelTestBase
  {
    private Mock<ITagManagerClient> _mockClient = null!;
    private Mock<IDialogService> _mockDialog = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<ITagManagerClient>();
      _mockDialog = new Mock<IDialogService>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new TagManagerViewModel(MockContext!, _mockClient.Object, _mockDialog.Object);
      _mockClient.Verify(x => x.GetTagsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new TagManagerViewModel(MockContext!, _mockClient.Object, _mockDialog.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.TagManager, vm.PanelId);
      Assert.IsNotNull(vm.LoadTagsCommand);
      Assert.IsNotNull(vm.LoadCategoriesCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new TagManagerViewModel(MockContext!, null!, _mockDialog.Object);
    }
  }
}
