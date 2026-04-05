using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Tests.Fixtures;

namespace VoiceStudio.App.Tests
{
    /// <summary>
    /// Assembly-level test initialization and cleanup.
    /// Ensures AppServices is initialized before any tests run.
    /// </summary>
    [TestClass]
    public static class TestAssemblySetup
    {
        /// <summary>
        /// Called once before any tests in the assembly run.
        /// Initializes AppServices with test-appropriate services.
        /// </summary>
        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            TestAppServicesHelper.EnsureInitialized();
        }

        /// <summary>
        /// Called once after all tests in the assembly have run.
        /// Cleans up shared resources.
        /// </summary>
        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
        TestAppServicesHelper.Cleanup();
        // Encourage teardown of remaining COM/dispatcher resources to avoid testhost crash.
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        }
    }
}
