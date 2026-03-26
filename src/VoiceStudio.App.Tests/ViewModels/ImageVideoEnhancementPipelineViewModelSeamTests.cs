using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for ImageVideoEnhancementPipelineViewModel.
  /// Instantiates ViewModel with mocked IImageVideoEnhancementPipelineClient.
  /// Supports "ImageVideoEnhancementPipelineViewModel migrated to IImageVideoEnhancementPipelineClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ImageVideoEnhancementPipelineViewModelSeamTests
  {
    private Mock<IImageVideoEnhancementPipelineClient> _mockClient = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IImageVideoEnhancementPipelineClient>();
      _mockClient
          .Setup(x => x.ApplyPipelineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyDictionary<string, Dictionary<string, object>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .Returns(System.Threading.Tasks.Task.CompletedTask);
      _mockClient
          .Setup(x => x.PreviewPipelineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyDictionary<string, Dictionary<string, object>>>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new Dictionary<string, object> { { "original_quality", 70.0 }, { "enhanced_quality", 85.0 } });
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new ImageVideoEnhancementPipelineViewModel(_mockClient.Object);

      _mockClient.Verify(
          x => x.ApplyPipelineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyDictionary<string, Dictionary<string, object>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _mockClient.Verify(
          x => x.PreviewPipelineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyDictionary<string, Dictionary<string, object>>>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new ImageVideoEnhancementPipelineViewModel(_mockClient.Object);

      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.SavePresetCommand);
      Assert.IsNotNull(vm.ApplyPipelineCommand);
      Assert.IsNotNull(vm.SelectFilesCommand);
      Assert.IsNotNull(vm.PreviewPipelineCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new ImageVideoEnhancementPipelineViewModel(null!);
    }
  }
}
