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
  /// Seam-aware tests for EmotionStylePresetEditorViewModel.
  /// Instantiates ViewModel with mocked IEmotionControlClient.
  /// Supports "EmotionStylePresetEditorViewModel migrated to IEmotionControlClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EmotionStylePresetEditorViewModelSeamTests
  {
    private Mock<IEmotionControlClient> _mockClient = null!;
    private IVoiceSynthesisService _voiceSynthesisService = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IEmotionControlClient>();
      _voiceSynthesisService = Mock.Of<IVoiceSynthesisService>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before LoadPresetsAsync.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new EmotionStylePresetEditorViewModel(_mockClient.Object, _voiceSynthesisService);
      _mockClient.Verify(x => x.GetPresetsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new EmotionStylePresetEditorViewModel(_mockClient.Object, _voiceSynthesisService);
      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.CreatePresetCommand);
      Assert.IsNotNull(vm.SavePresetCommand);
      Assert.IsNotNull(vm.DeletePresetCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new EmotionStylePresetEditorViewModel(null!, _voiceSynthesisService);
    }
  }
}
