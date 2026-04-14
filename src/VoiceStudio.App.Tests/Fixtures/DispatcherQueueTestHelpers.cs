using System;
using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace VoiceStudio.App.Tests.Fixtures
{
    /// <summary>
    /// Bounded shutdown for <see cref="DispatcherQueueController"/> in unit tests.
    /// Unbounded <c>ShutdownQueueAsync().GetAwaiter().GetResult()</c> can deadlock on headless/CI runners
    /// and cause whole-shard harness timeouts (e.g. ViewModels Seam A-D).
    /// </summary>
    public static class DispatcherQueueTestHelpers
    {
        /// <summary>Default wait aligned with <see cref="TestAppServicesHelper"/> dispatcher teardown.</summary>
        public const int DefaultShutdownWaitMs = 15_000;

        /// <summary>
        /// Synchronously shuts down the controller with a hard wall-clock bound.
        /// </summary>
        /// <param name="controller">May be null (no-op).</param>
        /// <param name="waitMs">Maximum time to wait for graceful shutdown.</param>
        /// <exception cref="TimeoutException">When shutdown does not complete in time.</exception>
        public static void ShutdownSyncBounded(DispatcherQueueController? controller, int waitMs = DefaultShutdownWaitMs)
        {
            if (controller is null)
            {
                return;
            }

            try
            {
                var task = controller.ShutdownQueueAsync().AsTask();
                if (!task.Wait(waitMs))
                {
                    throw new TimeoutException(
                        "DispatcherQueueController.ShutdownQueueAsync did not complete within " + waitMs +
                        "ms (possible deadlock or stalled dispatcher thread on headless runner).");
                }
            }
            catch (AggregateException ex) when (ex.InnerException is not null)
            {
                Debug.WriteLine("DispatcherQueueTestHelpers: shutdown failed: " + ex.InnerException.Message);
                throw ex.InnerException;
            }
        }
    }
}
