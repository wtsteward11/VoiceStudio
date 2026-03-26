using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TagOrganizationViewModel.
  /// Instantiates ViewModel with mocked ITagOrganizationClient and IProfilesClient.
  /// Supports "TagOrganizationViewModel migrated to ITagOrganizationClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TagOrganizationViewModelSeamTests
  {
    private Mock<ITagOrganizationClient> _mockTagClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockTagClient = new Mock<ITagOrganizationClient>();
      _mockTagClient
          .Setup(x => x.UpdateTagAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Returns(System.Threading.Tasks.Task.CompletedTask);
      _mockProfilesClient = new Mock<IProfilesClient>();
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new System.Collections.Generic.List<VoiceStudio.Core.Models.VoiceProfile>());
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new TagOrganizationViewModel(_mockTagClient.Object, _mockProfilesClient.Object);

      _mockTagClient.Verify(
          x => x.UpdateTagAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _mockProfilesClient.Verify(
          x => x.GetProfilesAsync(It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new TagOrganizationViewModel(_mockTagClient.Object, _mockProfilesClient.Object);

      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.RefreshCommand);
      Assert.AreEqual("Cloud", vm.ViewMode);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullTagClient_Throws()
    {
      _ = new TagOrganizationViewModel(null!, _mockProfilesClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProfilesClient_Throws()
    {
      _ = new TagOrganizationViewModel(_mockTagClient.Object, null!);
    }
  }
}
