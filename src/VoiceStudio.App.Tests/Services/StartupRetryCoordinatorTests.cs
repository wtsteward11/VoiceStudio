// VoiceStudio - StartupRetryCoordinator unit tests (Premium Proof Closure C2)

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Unit tests for StartupRetryCoordinator. Verifies RetryAsync sets BackendStarting,
/// calls EnsureBackendRunningAsync, and sets BackendFailed when still starting after failure.
/// </summary>
[TestClass]
[TestCategory("Services")]
public class StartupRetryCoordinatorTests
{
    [TestMethod]
    public void RetryAsync_SetsBackendStarting_BeforeEnsureBackendRunning()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var ensureCalled = false;
        var startupSetBeforeEnsure = false;
        Func<Task<bool>> ensureBackend = async () =>
        {
            ensureCalled = true;
            startupSetBeforeEnsure = startupMock.Invocations.Count >= 1; // SetBackendStarting was called
            await Task.Yield();
            return false;
        };

        var sut = new StartupRetryCoordinator(startupMock.Object, ensureBackend, null);
        sut.RetryAsync().GetAwaiter().GetResult();

        startupMock.Verify(x => x.SetBackendStarting(), Times.Once);
        Assert.IsTrue(ensureCalled, "EnsureBackendRunning should have been called");
        Assert.IsTrue(startupSetBeforeEnsure, "SetBackendStarting should be called before EnsureBackendRunning");
    }

    [TestMethod]
    public void RetryAsync_WhenEnsureFails_AndStillBackendStarting_SetsBackendFailed()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var sut = new StartupRetryCoordinator(startupMock.Object, () => Task.FromResult(false), null);
        sut.RetryAsync().GetAwaiter().GetResult();

        startupMock.Verify(x => x.SetBackendStarting(), Times.Once);
        startupMock.Verify(x => x.SetBackendFailed(It.Is<string>(s => s.Contains("Backend failed to start"))), Times.Once);
    }

    [TestMethod]
    public void RetryAsync_WhenPortCollision_DoesNotRetry()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var ensureCallCount = 0;
        var sut = new StartupRetryCoordinator(
            startupMock.Object,
            () => { ensureCallCount++; return Task.FromResult(false); },
            () => new BackendStartFailedEventArgs(BackendStartFailureCategory.PortCollision, "Port 8000 in use"));
        sut.RetryAsync().GetAwaiter().GetResult();

        Assert.AreEqual(1, ensureCallCount, "Port collision should not retry");
        startupMock.Verify(x => x.SetBackendFailed(It.Is<string>(s => s.Contains("Port 8000"))), Times.Once);
    }

    [TestMethod]
    public void RetryAsync_WhenRuntimeMissing_SetsBackendFailedNoRetry()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var ensureCallCount = 0;
        var sut = new StartupRetryCoordinator(
            startupMock.Object,
            () => { ensureCallCount++; return Task.FromResult(false); },
            () => new BackendStartFailedEventArgs(BackendStartFailureCategory.RuntimeMissing, "Python runtime not found"));
        sut.RetryAsync().GetAwaiter().GetResult();

        Assert.AreEqual(1, ensureCallCount, "Runtime missing should not retry");
        startupMock.Verify(x => x.SetBackendFailed(It.Is<string>(s => s.Contains("Python runtime"))), Times.Once);
    }

    [TestMethod]
    public void RetryAsync_WhenHealthTimeout_RetriesUpToN()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var ensureCallCount = 0;
        var sut = new StartupRetryCoordinator(
            startupMock.Object,
            () => { ensureCallCount++; return Task.FromResult(false); },
            () => new BackendStartFailedEventArgs(BackendStartFailureCategory.HealthTimeout, "Backend did not become healthy"),
            retryDelayOverride: TimeSpan.FromMilliseconds(10));
        sut.RetryAsync().GetAwaiter().GetResult();

        Assert.IsTrue(ensureCallCount >= 2, "Health timeout should retry at least twice (initial + 2 retries)");
        startupMock.Verify(x => x.SetBackendFailed(It.Is<string>(s => s.Contains("Backend did not become healthy"))), Times.Once);
    }

    [TestMethod]
    public void RetryAsync_WhenEnsureSucceeds_DoesNotSetBackendFailed()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendReady);

        var sut = new StartupRetryCoordinator(startupMock.Object, () => Task.FromResult(true), null);
        sut.RetryAsync().GetAwaiter().GetResult();

        startupMock.Verify(x => x.SetBackendStarting(), Times.Once);
        startupMock.Verify(x => x.SetBackendFailed(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void RetryAsync_WhenEnsureSucceeds_StateBecomesBackendReady()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var sut = new StartupRetryCoordinator(startupMock.Object, () => Task.FromResult(true), null);
        sut.RetryAsync().GetAwaiter().GetResult();

        startupMock.Verify(x => x.SetBackendStarting(), Times.Once);
        startupMock.Verify(x => x.SetBackendFailed(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void RetryAsync_WhenInvalidAppRoot_DoesNotRetry_AppendsNoRetryExplanation()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var ensureCallCount = 0;
        var sut = new StartupRetryCoordinator(
            startupMock.Object,
            () => { ensureCallCount++; return Task.FromResult(false); },
            () => new BackendStartFailedEventArgs(BackendStartFailureCategory.InvalidAppRoot, "Invalid app root path"));
        sut.RetryAsync().GetAwaiter().GetResult();

        Assert.AreEqual(1, ensureCallCount, "InvalidAppRoot should not retry");
        startupMock.Verify(x => x.SetBackendFailed(It.Is<string>(s =>
            s.Contains("Invalid app root path") && s.Contains("Retry will not help") && s.Contains("Reinstall"))), Times.Once);
    }

    [TestMethod]
    public void RetryAsync_WhenSpawnFailure_DoesNotRetry_AppendsRetryMayHelp()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var ensureCallCount = 0;
        var sut = new StartupRetryCoordinator(
            startupMock.Object,
            () => { ensureCallCount++; return Task.FromResult(false); },
            () => new BackendStartFailedEventArgs(BackendStartFailureCategory.SpawnFailure, "Process spawn failed"));
        sut.RetryAsync().GetAwaiter().GetResult();

        Assert.AreEqual(1, ensureCallCount, "SpawnFailure should not retry (no bounded retry)");
        startupMock.Verify(x => x.SetBackendFailed(It.Is<string>(s =>
            s.Contains("Process spawn failed") && s.Contains("Retry may help"))), Times.Once);
    }

    [TestMethod]
    public void RetryAsync_WhenHealthTimeout_SucceedsOnSecondAttempt_DoesNotSetBackendFailed()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var attempt = 0;
        var sut = new StartupRetryCoordinator(
            startupMock.Object,
            () =>
            {
                attempt++;
                return Task.FromResult(attempt >= 2);
            },
            () => new BackendStartFailedEventArgs(BackendStartFailureCategory.HealthTimeout, "Backend did not become healthy"),
            retryDelayOverride: TimeSpan.FromMilliseconds(10));
        sut.RetryAsync().GetAwaiter().GetResult();

        Assert.AreEqual(2, attempt, "Should have tried twice (initial + 1 retry)");
        startupMock.Verify(x => x.SetBackendFailed(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void RetryAsync_WhenGetLastFailureReturnsNull_UsesSpawnFailureFallback()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var ensureCallCount = 0;
        var sut = new StartupRetryCoordinator(
            startupMock.Object,
            () => { ensureCallCount++; return Task.FromResult(false); },
            () => null);
        sut.RetryAsync().GetAwaiter().GetResult();

        Assert.AreEqual(1, ensureCallCount, "Null lastFailure should not retry (SpawnFailure fallback)");
        startupMock.Verify(x => x.SetBackendFailed(It.Is<string>(s =>
            s.Contains("Backend failed to start") || s.Contains("Retry may help"))), Times.Once);
    }

    [TestMethod]
    public void RetryAsync_WhenProgressProvided_ReportsAttemptDuringRetry()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(x => x.CurrentState).Returns(StartupState.BackendStarting);

        var reported = new System.Collections.Generic.List<StartupRetryProgress>();
        var progress = new Progress<StartupRetryProgress>(p => reported.Add(p));

        var sut = new StartupRetryCoordinator(
            startupMock.Object,
            () => Task.FromResult(false),
            () => new BackendStartFailedEventArgs(BackendStartFailureCategory.HealthTimeout, "Timeout"),
            retryDelayOverride: TimeSpan.FromMilliseconds(10));
        sut.RetryAsync(progress).GetAwaiter().GetResult();

        Assert.IsTrue(reported.Count >= 1, "Progress should report at least one attempt");
        Assert.IsTrue(reported.Any(p => p.Message.Contains("Retrying") && p.Message.Contains("attempt")), "Message should include attempt info");
    }
}
