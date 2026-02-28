using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
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
        private static DispatcherQueueController? _dispatcherController;
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

                // Create dispatcher on dedicated thread (required for DispatcherQueueTimer)
                _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
                var dispatcher = _dispatcherController.DispatcherQueue;
                var context = new ViewModelContext(NullLogger.Instance, dispatcher);

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
        /// Gets the dispatcher queue used for tests.
        /// Call EnsureInitialized() before using this.
        /// </summary>
        public static DispatcherQueue? GetDispatcher()
        {
            return _dispatcherController?.DispatcherQueue;
        }

        /// <summary>
        /// Cleans up the dispatcher controller.
        /// Call this in [AssemblyCleanup] or at the end of test runs.
        /// Note: AppServices cannot be reset, so tests share the same instance.
        /// </summary>
        public static void Cleanup()
        {
            if (_dispatcherController != null)
            {
                _dispatcherController.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
                _dispatcherController = null;
            }
        }
    }
}
