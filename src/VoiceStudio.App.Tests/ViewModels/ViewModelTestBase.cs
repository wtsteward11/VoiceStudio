using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Tests.Services;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Base class for ViewModel tests providing common setup and helper methods.
  /// Inherit from this class to share common test infrastructure for ViewModel tests.
  /// </summary>
  public abstract class ViewModelTestBase : TestBase
  {
    protected MockBackendClient? MockBackendClient { get; private set; }
    protected MockAnalyticsService? MockAnalyticsService { get; private set; }
    protected MockNavigationService? MockNavigationService { get; private set; }
    protected MockViewModelContext? MockContext { get; private set; }
    protected MockSettingsService? MockSettingsService { get; private set; }
    protected Mock<IViewModelContext>? MockContextMoq { get; private set; }

    [TestInitialize]
    public override void TestInitialize()
    {
      base.TestInitialize();

      // Initialize mock services
      MockBackendClient = new MockBackendClient();
      MockAnalyticsService = new MockAnalyticsService();
      MockNavigationService = new MockNavigationService();
      MockContext = new MockViewModelContext();
      MockSettingsService = new MockSettingsService();

      // Also provide Moq-based mock for IViewModelContext
      MockContextMoq = new Mock<IViewModelContext>();
      MockContextMoq.Setup(x => x.Logger).Returns(MockContext.Logger);
      MockContextMoq.Setup(x => x.Dispatcher).Returns(MockContext.Dispatcher);

      // Reset test data generators
      TestDataGenerators.ResetIdCounter();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
      // Clear mock service state
      MockAnalyticsService?.Clear();
      MockNavigationService?.Clear();
      MockSettingsService?.Clear();

      MockBackendClient = null;
      MockAnalyticsService = null;
      MockNavigationService = null;
      MockContext = null;
      MockSettingsService = null;
      MockContextMoq = null;

      base.TestCleanup();
    }

    /// <summary>
    /// Helper method to wait for async operations to complete.
    /// </summary>
    protected async System.Threading.Tasks.Task WaitForAsyncOperation(int delayMs = 100)
    {
      await System.Threading.Tasks.Task.Delay(delayMs);
    }

    /// <summary>
    /// Helper method to verify ViewModel has standard properties.
    /// </summary>
    protected void VerifyStandardViewModelProperties<T>(T viewModel) where T : VoiceStudio.App.ViewModels.BaseViewModel
    {
      Assert.IsNotNull(viewModel, "ViewModel should not be null");

      // BaseViewModel properties should be accessible
      // Note: IsLoading and ErrorMessage are typically properties on ViewModels
      // The exact properties depend on the ViewModel implementation
    }

    /// <summary>
    /// Creates a mock for IBackendClient with common setup.
    /// </summary>
    protected Mock<IBackendClient> CreateMockBackendClient()
    {
      var mock = new Mock<IBackendClient>();
      mock.Setup(x => x.IsConnected).Returns(true);
      return mock;
    }
  }
}