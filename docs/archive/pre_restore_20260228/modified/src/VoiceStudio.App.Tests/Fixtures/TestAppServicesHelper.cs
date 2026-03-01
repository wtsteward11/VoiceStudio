using Microsoft.Extensions.DependencyInjection;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Fixtures
{
    /// <summary>
    /// Helper class to initialize AppServices for unit tests.
    /// Provides test-appropriate service registrations that allow ViewModels
    /// to be instantiated without requiring the full WinUI application context.
    /// </summary>
    public static class TestAppServicesHelper
    {
        private static bool _initialized;
        private static readonly object _lock = new();

        /// <summary>
        /// Ensures AppServices is initialized with test-appropriate services.
        /// This method is idempotent and thread-safe.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (_lock)
            {
                if (_initialized)
                    return;

                // Check if already properly initialized (with MultiSelectService)
                // We specifically check for MultiSelectService because that's the critical service
                // that most ViewModels require and was missing in previous test setups.
                try
                {
                    var existingContext = AppServices.GetService<IViewModelContext>();
                    var existingMultiSelect = AppServices.GetService<MultiSelectService>();
                    if (existingContext != null && existingMultiSelect != null)
                    {
                        _initialized = true;
                        return;
                    }
                }
                // ALLOWED: empty catch - Swallowing exception intentionally; AppServices not yet initialized
                catch
                {
                    // Not initialized at all, continue with initialization
                }

                // Use MockViewModelContext to avoid DispatcherQueue crash in MSTest environment
                var context = new MockViewModelContext();

                // Build service collection with required services
                var services = new ServiceCollection();

                // Core context
                services.AddSingleton<IViewModelContext>(context);

                // MultiSelectService - required by many ViewModels
                services.AddSingleton<MultiSelectService>();

                // EventAggregator - required for inter-panel communication testing
                services.AddSingleton<IEventAggregator, EventAggregator>();

                // WorkflowCoordinatorService - required for workflow orchestration testing
                services.AddSingleton<IWorkflowCoordinatorService, WorkflowCoordinatorService>();

                // Add other commonly needed services for tests
                // Note: Add more services here as needed based on test failures

                AppServices.Initialize(services.BuildServiceProvider());
                _initialized = true;
            }
        }

        /// <summary>
        /// Gets the mock dispatcher queue used for tests.
        /// Returns the MockDispatcherQueue which executes actions synchronously.
        /// Call EnsureInitialized() before using this.
        /// </summary>
        public static object? GetDispatcher()
        {
            var context = AppServices.GetService<IViewModelContext>();
            return context?.DispatcherQueue;
        }

        /// <summary>
        /// Cleans up test resources.
        /// Note: With MockViewModelContext, no special cleanup is required.
        /// </summary>
        public static void Cleanup()
        {
            // No dispatcher cleanup needed with MockViewModelContext
        }
    }
}
