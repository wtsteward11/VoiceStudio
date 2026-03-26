using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for EngineParameterTuningViewModel.
  /// Instantiates ViewModel with mocked IEngineParameterTuningClient.
  /// Supports "EngineParameterTuningViewModel migrated to IEngineParameterTuningClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EngineParameterTuningViewModelSeamTests
  {
    private Mock<IEngineParameterTuningClient> _mockClient = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockClient = new Mock<IEngineParameterTuningClient>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new EngineParameterTuningViewModel(_mockClient.Object);
      _mockClient.Verify(
        x => x.ConfigureEngineAsync(It.IsAny<string>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()),
        Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new EngineParameterTuningViewModel(_mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.SavePresetCommand);
      Assert.IsNotNull(vm.AutoOptimizeCommand);
      Assert.IsNotNull(vm.ResetToDefaultsCommand);
      Assert.IsNotNull(vm.ApplyParametersCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new EngineParameterTuningViewModel(null!);
    }
  }
}
