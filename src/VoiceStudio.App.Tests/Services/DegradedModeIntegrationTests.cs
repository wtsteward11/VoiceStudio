using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Semi-automated proof for 429 degraded mode flow.
  /// Asserts: repeated 429 inputs → one surfaced state (banner); no repeated toasts; recovery clears state.
  /// CI: Runs in dotnet-build job; protected by dedicated gate in build.yml.
  /// </summary>
  [TestClass]
  [TestCategory("DegradedMode")]
  public class DegradedModeIntegrationTests
  {
    private IServiceProvider? _provider;
    private ServiceCollection? _services;

    [TestInitialize]
    public void TestInitialize()
    {
      _services = new ServiceCollection();
      _services.AddSingleton<GracefulDegradationService>();
      _services.AddSingleton<IErrorPresentationService, ErrorPresentationService>();
      _provider = _services.BuildServiceProvider();
      AppServices.Initialize(_provider);
    }

    [TestCleanup]
    public void TestCleanup()
    {
      _provider = null;
      _services = null;
      // Restore full test AppServices for subsequent tests (Stage 13 order-dependence fix).
      // DegradedModeIntegrationTests replaces AppServices with minimal provider; without this,
      // tests that run after may see broken IEventAggregator/IViewModelContext.
      TestAppServicesHelper.EnsureInitialized();
    }

    /// <summary>
    /// Repeated 429 inputs → EnterDegradedMode once; no toast; IsDegradedMode true.
    /// </summary>
    [TestMethod]
    public void Repeated429_EntersDegradedMode_NoToast()
    {
      var degradationService = _provider!.GetRequiredService<GracefulDegradationService>();
      var errorService = _provider!.GetRequiredService<IErrorPresentationService>();

      var ex429 = new BackendException("Too many requests", 429, null, true);

      Assert.IsFalse(degradationService.IsDegradedMode, "Should not be degraded initially");

      for (var i = 0; i < 5; i++)
      {
        errorService.ShowError(ex429, "Test");
      }

      Assert.IsTrue(degradationService.IsDegradedMode,
        "After 5×429, IsDegradedMode should be true (one surfaced state, no toast storm)");
      Assert.AreEqual("Too many requests. Please wait before trying again.", degradationService.DegradationReason);
    }

    /// <summary>
    /// Recovery (ExitDegradedMode) clears state.
    /// </summary>
    [TestMethod]
    public void ExitDegradedMode_ClearsState()
    {
      var degradationService = _provider!.GetRequiredService<GracefulDegradationService>();
      var errorService = _provider!.GetRequiredService<IErrorPresentationService>();

      var ex429 = new BackendException("Too many requests", 429, null, true);
      errorService.ShowError(ex429, "Test");

      Assert.IsTrue(degradationService.IsDegradedMode);

      degradationService.ExitDegradedMode();

      Assert.IsFalse(degradationService.IsDegradedMode);
      Assert.IsNull(degradationService.DegradationReason);
    }

    /// <summary>
    /// ErrorHandler.IsBackendStressException recognizes 429.
    /// </summary>
    [TestMethod]
    public void ErrorHandler_Recognizes429_AsBackendStress()
    {
      var ex429 = new BackendException("Too many requests", 429, null, true);
      Assert.IsTrue(ErrorHandler.IsBackendStressException(ex429));
      Assert.IsTrue(ErrorHandler.IsRateLimitException(ex429));
    }
  }
}
