using System;
using System.Diagnostics;
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
                // is missing.
                try
                {
                    var existingContext = AppServices.GetService<IViewModelContext>();
                    var existingMultiSelect = AppServices.GetService<MultiSelectService>();
                    var existingEventAggregator = AppServices.GetService<IEventAggregator>();
                    if (existingContext != null && existingMultiSelect != null && existingEventAggregator != null)
                    {
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
            }
        }

        /// <summary>
        /// Replaces <see cref="AppServices"/> with a fresh default test provider and dispatcher.
        /// Use when a test temporarily calls <c>AppServices.Initialize</c> with a minimal container that still
        /// exposes <see cref="IEventAggregator"/> — <see cref="EnsureInitialized"/> would incorrectly early-return
        /// and leave downstream tests (e.g. workflow coherence) on the wrong provider.
        /// </summary>
        public static void RebuildDefaultProvider()
        {
            lock (_lock)
            {
                if (_dispatcherController != null)
                {
                    try
                    {
                        _dispatcherController.ShutdownQueueAsync().AsTask().Wait(2000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"TestAppServicesHelper.RebuildDefaultProvider: shutdown failed: {ex.Message}");
                    }

                    _dispatcherController = null;
                }

                _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
                var dispatcher = _dispatcherController.DispatcherQueue;
                var context = new ViewModelContext(NullLogger.Instance, dispatcher);
                var services = new ServiceCollection();
                services.AddSingleton<IViewModelContext>(context);
                services.AddSingleton<MultiSelectService>();
                services.AddSingleton<IEventAggregator, EventAggregator>();
                services.AddSingleton<IWorkflowCoordinatorService, WorkflowCoordinatorService>();
                AppServices.Initialize(services.BuildServiceProvider());
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
        /// Rebuilds the test provider with <see cref="IContextManager"/> registered (e.g. GAP-026 activation sync tests).
        /// Call <see cref="RebuildDefaultProvider"/> in test cleanup to restore the default provider without context.
        /// </summary>
        public static void EnsureInitializedWithContextManager(IContextManager contextManager)
        {
            if (contextManager == null)
                throw new ArgumentNullException(nameof(contextManager));

            lock (_lock)
            {
                if (_dispatcherController != null)
                {
                    try
                    {
                        _dispatcherController.ShutdownQueueAsync().AsTask().Wait(2000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"TestAppServicesHelper.EnsureInitializedWithContextManager: shutdown failed: {ex.Message}");
                    }

                    _dispatcherController = null;
                }

                _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
                var dispatcher = _dispatcherController.DispatcherQueue;
                var context = new ViewModelContext(NullLogger.Instance, dispatcher);
                var services = new ServiceCollection();
                services.AddSingleton<IViewModelContext>(context);
                services.AddSingleton<MultiSelectService>();
                services.AddSingleton<IEventAggregator, EventAggregator>();
                services.AddSingleton<IWorkflowCoordinatorService, WorkflowCoordinatorService>();
                services.AddSingleton<IContextManager>(_ => contextManager);
                AppServices.Initialize(services.BuildServiceProvider());
            }
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
                try
                {
                    // Attempt graceful shutdown to reduce lingering dispatcher threads that can crash testhost.
                    _dispatcherController.ShutdownQueueAsync().AsTask().Wait(2000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TestAppServicesHelper.Cleanup: ShutdownQueueAsync failed: {ex.Message}");
                }
            }
            _dispatcherController = null;
        }
    }
}
