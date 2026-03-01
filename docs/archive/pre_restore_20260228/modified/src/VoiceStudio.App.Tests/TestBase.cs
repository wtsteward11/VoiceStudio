using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using VoiceStudio.App.Tests.Fixtures;

namespace VoiceStudio.App.Tests
{
  /// <summary>
  /// Base class for test classes that provides common setup and teardown logic.
  /// Inherit from this class to share common test infrastructure.
  /// </summary>
  [TestClass]
  public abstract class TestBase
  {
    /// <summary>
    /// Gets or sets the test context which provides information about and functionality for the current test run.
    /// </summary>
    public TestContext? TestContext { get; set; }

    [TestInitialize]
    public virtual void TestInitialize()
    {
      // Initialize AppServices with MockViewModelContext for tests that create ViewModels
      TestAppServicesHelper.EnsureInitialized();
      
      // Common initialization logic
      // Log test start
      TestContext?.WriteLine($"Starting test: {TestContext?.TestName}");
    }

    [TestCleanup]
    public virtual void TestCleanup()
    {
      // Cleanup AppServices
      TestAppServicesHelper.Cleanup();
      
      // Common cleanup logic
      // Log test completion
      TestContext?.WriteLine($"Completed test: {TestContext?.TestName}");
    }

    /// <summary>
    /// Helper method to create test data.
    /// Override in derived classes for specific test data creation.
    /// </summary>
    protected virtual void CreateTestData()
    {
      // Override in derived classes
    }

    /// <summary>
    /// Helper method to clean up test data.
    /// Override in derived classes for specific test data cleanup.
    /// </summary>
    protected virtual void CleanupTestData()
    {
      // Override in derived classes
    }
  }
}