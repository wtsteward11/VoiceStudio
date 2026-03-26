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
        /// Re-initializes when DegradedModeIntegrationTests or similar replace AppServices with
        /// a minimal provider (no IEventAggregator), so WorkflowCoordinatorServiceTests and
        /// other event-based tests receive a valid EventAggregator regardless of test order.
        /// </summary>
        public static void EnsureInitialized()
        {
            lock (_lock)
            {
                // Always check for required services first. DegradedModeIntegrationTests replaces
                // AppServices with a minimal provider; we must re-initialize when EventAggregator
                // is missing. Do NOT early-return on _initialized — that would skip this check.
                try
                {
                    var existingContext = AppServices.GetService<IViewModelContext>();
                    var existingMultiSelect = AppServices.GetService<MultiSelectService>();
                    var existingEventAggregator = AppServices.GetService<IEventAggregator>();
                    if (existingContext != null && existingMultiSelect != null && existingEventAggregator != null)
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
        /// Hardened: Skip ShutdownQueueAsync to avoid testhost crash during teardown (Stage 13 full-harness fix).
        /// The dispatcher thread is abandoned; process exit will terminate it. ShutdownQueueAsync was
        /// causing testhost process crash when run after many tests (Services shard, full harness).
        /// </summary>
        public static void Cleanup()
        {
            _dispatcherController = null;
        }
    }
}
