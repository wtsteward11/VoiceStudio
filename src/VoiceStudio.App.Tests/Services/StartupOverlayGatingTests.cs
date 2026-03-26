using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services
{
    /// <summary>
    /// Verifies that backend-dependent commands are blocked when startup overlay is shown (Task 4).
    /// See docs/design/STARTUP_ORCHESTRATION_HARDENING_PLAN.md Round 3.
    /// </summary>
    [TestClass]
    public class StartupOverlayGatingTests
    {
        [TestMethod]
        public async Task ExecuteAsync_WhenNotReady_BlocksFileImport()
        {
            var mockStartup = new Mock<IStartupStateService>();
            mockStartup.Setup(x => x.IsReady).Returns(false);

            var registry = new UnifiedCommandRegistry(null, mockStartup.Object);
            var handlerCalled = false;
            registry.Register(
                new CommandDescriptor { Id = CommandIds.FileImport, Title = "Import" },
                (_, _) => { handlerCalled = true; return Task.CompletedTask; });

            await registry.ExecuteAsync(CommandIds.FileImport);

            Assert.IsFalse(handlerCalled, "file.import should be blocked when backend not ready");
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenNotReady_BlocksSynthesisGenerate()
        {
            var mockStartup = new Mock<IStartupStateService>();
            mockStartup.Setup(x => x.IsReady).Returns(false);

            var registry = new UnifiedCommandRegistry(null, mockStartup.Object);
            var handlerCalled = false;
            registry.Register(
                new CommandDescriptor { Id = CommandIds.SynthesisGenerate, Title = "Synthesize" },
                (_, _) => { handlerCalled = true; return Task.CompletedTask; });

            await registry.ExecuteAsync(CommandIds.SynthesisGenerate);

            Assert.IsFalse(handlerCalled, "synthesis.generate should be blocked when backend not ready");
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenNotReady_BlocksPanelLibrary()
        {
            var mockStartup = new Mock<IStartupStateService>();
            mockStartup.Setup(x => x.IsReady).Returns(false);

            var registry = new UnifiedCommandRegistry(null, mockStartup.Object);
            var handlerCalled = false;
            registry.Register(
                new CommandDescriptor { Id = CommandIds.PanelLibrary, Title = "Library" },
                (_, _) => { handlerCalled = true; return Task.CompletedTask; });

            await registry.ExecuteAsync(CommandIds.PanelLibrary);

            Assert.IsFalse(handlerCalled, "panel.library should be blocked when backend not ready");
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenReady_ExecutesFileImport()
        {
            var mockStartup = new Mock<IStartupStateService>();
            mockStartup.Setup(x => x.IsReady).Returns(true);

            var registry = new UnifiedCommandRegistry(null, mockStartup.Object);
            var handlerCalled = false;
            registry.Register(
                new CommandDescriptor { Id = CommandIds.FileImport, Title = "Import" },
                (_, _) => { handlerCalled = true; return Task.CompletedTask; });

            await registry.ExecuteAsync(CommandIds.FileImport);

            Assert.IsTrue(handlerCalled, "file.import should execute when backend ready");
        }

        /// <summary>
        /// Round 4 Task 1: Transport play should be blocked when backend not ready.
        /// </summary>
        [TestMethod]
        public void ShouldBlockTransportPlayback_WhenNotReady_ReturnsTrue()
        {
            var mockStartup = new Mock<IStartupStateService>();
            mockStartup.Setup(x => x.IsReady).Returns(false);

            var result = StartupGatingHelper.ShouldBlockTransportPlayback(mockStartup.Object);

            Assert.IsTrue(result, "Transport should be blocked when backend not ready");
        }

        /// <summary>
        /// Round 4 Task 1: Transport play should not be blocked when backend ready.
        /// </summary>
        [TestMethod]
        public void ShouldBlockTransportPlayback_WhenReady_ReturnsFalse()
        {
            var mockStartup = new Mock<IStartupStateService>();
            mockStartup.Setup(x => x.IsReady).Returns(true);

            var result = StartupGatingHelper.ShouldBlockTransportPlayback(mockStartup.Object);

            Assert.IsFalse(result, "Transport should not be blocked when backend ready");
        }

        /// <summary>
        /// Round 4 Task 1: Panel init should be deferred when backend not ready.
        /// </summary>
        [TestMethod]
        public void ShouldDeferPanelInit_WhenNotReady_ReturnsTrue()
        {
            var mockStartup = new Mock<IStartupStateService>();
            mockStartup.Setup(x => x.IsReady).Returns(false);

            var result = StartupGatingHelper.ShouldDeferPanelInit(mockStartup.Object);

            Assert.IsTrue(result, "Panel init should be deferred when backend not ready");
        }

        /// <summary>
        /// Round 4 Task 1: Panel init should not be deferred when backend ready.
        /// </summary>
        [TestMethod]
        public void ShouldDeferPanelInit_WhenReady_ReturnsFalse()
        {
            var mockStartup = new Mock<IStartupStateService>();
            mockStartup.Setup(x => x.IsReady).Returns(true);

            var result = StartupGatingHelper.ShouldDeferPanelInit(mockStartup.Object);

            Assert.IsFalse(result, "Panel init should not be deferred when backend ready");
        }

        #region Panel-init deferral (StartupGatingHelper.WaitForBackendReadyThenAsync)

        /// <summary>
        /// Round 5 Task 3: Panel init deferral — init action not called until backend ready.
        /// </summary>
        [TestMethod]
        public async Task WaitForBackendReadyThenAsync_WhenNotReady_DoesNotCallInitUntilReady()
        {
            var startupState = new StartupStateService();
            startupState.SetBackendStarting();
            var initCalled = false;

            var waitTask = StartupGatingHelper.WaitForBackendReadyThenAsync(startupState, async () =>
            {
                initCalled = true;
                await Task.CompletedTask;
            });

            Assert.IsFalse(initCalled, "Init should not be called before ready");
            startupState.SetBackendReady();
            await waitTask;
            Assert.IsTrue(initCalled, "Init should be called after SetBackendReady");
        }

        /// <summary>
        /// Round 5 Task 3: Panel init deferral — no deadlock when backend fails.
        /// </summary>
        [TestMethod]
        public async Task WaitForBackendReadyThenAsync_WhenBackendFails_DoesNotDeadlock()
        {
            var startupState = new StartupStateService();
            startupState.SetBackendStarting();
            var initCalled = false;

            var waitTask = StartupGatingHelper.WaitForBackendReadyThenAsync(startupState, async () =>
            {
                initCalled = true;
                await Task.CompletedTask;
            });

            await Task.Delay(50);
            startupState.SetBackendFailed("test failure");

            var completed = await Task.WhenAny(waitTask, Task.Delay(2000)) == waitTask;
            Assert.IsTrue(completed, "WaitForBackendReadyThenAsync should complete on BackendFailed (no deadlock)");
            await waitTask;
            Assert.IsTrue(initCalled, "Init should still be called after BackendFailed (panels load, overlay shows)");
        }

        #endregion
    }
}
