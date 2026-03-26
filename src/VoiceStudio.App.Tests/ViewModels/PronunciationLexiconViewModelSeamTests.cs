using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for PronunciationLexiconViewModel.
  /// Instantiates ViewModel with mocked IPronunciationLexiconClient.
  /// Supports "PronunciationLexiconViewModel migrated to IPronunciationLexiconClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class PronunciationLexiconViewModelSeamTests : ViewModelTestBase
  {
    private Mock<IPronunciationLexiconClient> _mockClient = null!;
    private Mock<IProfilesClient> _mockProfiles = null!;
    private IVoiceSynthesisService _voiceSynthesisService = null!;
    private IAudioPlayerService _audioPlayer = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();
      _mockClient = new Mock<IPronunciationLexiconClient>();
      _mockProfiles = new Mock<IProfilesClient>();
      _voiceSynthesisService = Mock.Of<IVoiceSynthesisService>();
      _audioPlayer = Mock.Of<IAudioPlayerService>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new PronunciationLexiconViewModel(MockContext!, _mockClient.Object, _mockProfiles.Object, _voiceSynthesisService, _audioPlayer);
      _mockClient.Verify(x => x.GetEntriesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new PronunciationLexiconViewModel(MockContext!, _mockClient.Object, _mockProfiles.Object, _voiceSynthesisService, _audioPlayer);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.PronunciationLexicon, vm.PanelId);
      Assert.IsNotNull(vm.LoadEntriesCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new PronunciationLexiconViewModel(MockContext!, null!, _mockProfiles.Object, _voiceSynthesisService, _audioPlayer);
    }
  }
}
