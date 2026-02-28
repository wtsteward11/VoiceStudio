using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public class DeferredServiceInitializerTests : TestBase
{
    private DeferredServiceInitializer _initializer = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _initializer = new DeferredServiceInitializer();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        _initializer = null!;
        base.TestCleanup();
    }

    #region Initial State Tests

    [TestMethod]
    public void InitialState_IsInitialized_IsFalse()
    {
        // Assert
        Assert.IsFalse(_initializer.IsInitialized);
    }

    #endregion

    #region Register Tests

    [TestMethod]
    public void Register_WithFactory_AddsService()
    {
        // Arrange
        var initialized = false;

        // Act
        _initializer.Register<string>(
            "TestService",
            () => "instance",
            _ => initialized = true);

        // Assert - service is registered but not initialized yet
        Assert.IsFalse(initialized);
        Assert.IsFalse(_initializer.IsInitialized);
    }

    [TestMethod]
    public void RegisterAsync_AddsAsyncService()
    {
        // Arrange
        var initialized = false;

        // Act
        _initializer.RegisterAsync(
            "AsyncService",
            async ct =>
            {
                await Task.Delay(1, ct);
                initialized = true;
            });

        // Assert - service is registered but not initialized yet
        Assert.IsFalse(initialized);
        Assert.IsFalse(_initializer.IsInitialized);
    }

    #endregion

    #region InitializeAllAsync Tests

    [TestMethod]
    public async Task InitializeAllAsync_WithNoServices_Completes()
    {
        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsTrue(_initializer.IsInitialized);
    }

    [TestMethod]
    public async Task InitializeAllAsync_InitializesAllRegisteredServices()
    {
        // Arrange
        var service1Initialized = false;
        var service2Initialized = false;

        _initializer.Register<object>(
            "Service1",
            () => new object(),
            _ => service1Initialized = true);

        _initializer.RegisterAsync(
            "Service2",
            async ct =>
            {
                await Task.Delay(1, ct);
                service2Initialized = true;
            });

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsTrue(service1Initialized);
        Assert.IsTrue(service2Initialized);
        Assert.IsTrue(_initializer.IsInitialized);
    }

    [TestMethod]
    public async Task InitializeAllAsync_CalledTwice_OnlyInitializesOnce()
    {
        // Arrange
        var initCount = 0;

        _initializer.Register<object>(
            "CountingService",
            () => new object(),
            _ => initCount++);

        // Act
        await _initializer.InitializeAllAsync();
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.AreEqual(1, initCount);
    }

    [TestMethod]
    public async Task InitializeAllAsync_ServiceThrowsException_ContinuesWithOthers()
    {
        // Arrange
        var service1Initialized = false;
        var service3Initialized = false;

        _initializer.Register<object>(
            "Service1",
            () => new object(),
            _ => service1Initialized = true,
            ServicePriority.High);

        _initializer.Register<object>(
            "FailingService",
            () => throw new InvalidOperationException("Test failure"),
            _ => { },
            ServicePriority.Normal);

        _initializer.Register<object>(
            "Service3",
            () => new object(),
            _ => service3Initialized = true,
            ServicePriority.Low);

        // Act
        await _initializer.InitializeAllAsync();

        // Assert - other services still initialized
        Assert.IsTrue(service1Initialized);
        Assert.IsTrue(service3Initialized);
        Assert.IsTrue(_initializer.IsInitialized);
    }

    [TestMethod]
    public async Task InitializeAllAsync_AsyncServiceThrowsException_ContinuesWithOthers()
    {
        // Arrange
        var service1Initialized = false;
        var service3Initialized = false;

        _initializer.RegisterAsync(
            "Service1",
            async ct =>
            {
                await Task.Delay(1, ct);
                service1Initialized = true;
            },
            ServicePriority.High);

        _initializer.RegisterAsync(
            "FailingService",
            async ct =>
            {
                await Task.Delay(1, ct);
                throw new InvalidOperationException("Test failure");
            },
            ServicePriority.Normal);

        _initializer.RegisterAsync(
            "Service3",
            async ct =>
            {
                await Task.Delay(1, ct);
                service3Initialized = true;
            },
            ServicePriority.Low);

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsTrue(service1Initialized);
        Assert.IsTrue(service3Initialized);
        Assert.IsTrue(_initializer.IsInitialized);
    }

    [TestMethod]
    public async Task InitializeAllAsync_RespectsPriority_HighBeforeLow()
    {
        // Arrange
        var initOrder = new List<string>();

        _initializer.RegisterAsync(
            "LowPriority",
            async ct =>
            {
                await Task.Delay(1, ct);
                initOrder.Add("Low");
            },
            ServicePriority.Low);

        _initializer.RegisterAsync(
            "HighPriority",
            async ct =>
            {
                await Task.Delay(1, ct);
                initOrder.Add("High");
            },
            ServicePriority.High);

        _initializer.RegisterAsync(
            "NormalPriority",
            async ct =>
            {
                await Task.Delay(1, ct);
                initOrder.Add("Normal");
            },
            ServicePriority.Normal);

        _initializer.RegisterAsync(
            "CriticalPriority",
            async ct =>
            {
                await Task.Delay(1, ct);
                initOrder.Add("Critical");
            },
            ServicePriority.Critical);

        // Act
        await _initializer.InitializeAllAsync();

        // Assert - should be in priority order: Critical, High, Normal, Low
        Assert.AreEqual(4, initOrder.Count);
        Assert.AreEqual("Critical", initOrder[0]);
        Assert.AreEqual("High", initOrder[1]);
        Assert.AreEqual("Normal", initOrder[2]);
        Assert.AreEqual("Low", initOrder[3]);
    }

    [TestMethod]
    public async Task InitializeAllAsync_WithCancellation_StopsInitialization()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var initCount = 0;

        _initializer.RegisterAsync(
            "Service1",
            async ct =>
            {
                initCount++;
                cts.Cancel(); // Cancel after first service starts
                ct.ThrowIfCancellationRequested(); // This will throw
                await Task.Delay(100, ct);
            },
            ServicePriority.High);

        _initializer.RegisterAsync(
            "Service2",
            async ct =>
            {
                initCount++;
                await Task.Delay(1, ct);
            },
            ServicePriority.Normal);

        // Act - expect TaskCanceledException or OperationCanceledException
        try
        {
            await _initializer.InitializeAllAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }

        // Assert - second service should not have initialized
        Assert.AreEqual(1, initCount);
    }

    #endregion

    #region Event Tests

    [TestMethod]
    public async Task InitializeAllAsync_RaisesInitializationStartedEvent()
    {
        // Arrange
        var eventRaised = false;
        _initializer.InitializationStarted += (s, e) => eventRaised = true;

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsTrue(eventRaised);
    }

    [TestMethod]
    public async Task InitializeAllAsync_RaisesInitializationCompletedEvent()
    {
        // Arrange
        DeferredInitCompletedEventArgs? completedArgs = null;
        _initializer.InitializationCompleted += (s, e) => completedArgs = e;

        _initializer.Register<object>(
            "TestService",
            () => new object(),
            _ => { });

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsNotNull(completedArgs);
        Assert.IsTrue(completedArgs.TotalDurationMs >= 0);
        Assert.AreEqual(1, completedArgs.Results.Count);
    }

    [TestMethod]
    public async Task InitializeAllAsync_RaisesServiceInitializedEventForEachService()
    {
        // Arrange
        var initializedServices = new List<string>();
        _initializer.ServiceInitialized += (s, e) => initializedServices.Add(e.Result.ServiceName);

        _initializer.Register<object>("Service1", () => new object(), _ => { });
        _initializer.Register<object>("Service2", () => new object(), _ => { });
        _initializer.Register<object>("Service3", () => new object(), _ => { });

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.AreEqual(3, initializedServices.Count);
        CollectionAssert.Contains(initializedServices, "Service1");
        CollectionAssert.Contains(initializedServices, "Service2");
        CollectionAssert.Contains(initializedServices, "Service3");
    }

    [TestMethod]
    public async Task InitializeAllAsync_ServiceInitializedEvent_ContainsCorrectResult()
    {
        // Arrange
        ServiceInitResult? result = null;
        _initializer.ServiceInitialized += (s, e) => result = e.Result;

        _initializer.Register<object>(
            "MyService",
            () => new object(),
            _ => { });

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("MyService", result.ServiceName);
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.DurationMs >= 0);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task InitializeAllAsync_FailedService_ReportsErrorInResult()
    {
        // Arrange
        ServiceInitResult? result = null;
        _initializer.ServiceInitialized += (s, e) =>
        {
            if (e.Result.ServiceName == "FailingService")
                result = e.Result;
        };

        _initializer.Register<object>(
            "FailingService",
            () => throw new InvalidOperationException("Test error message"),
            _ => { });

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("FailingService", result.ServiceName);
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
        Assert.IsTrue(result.Error.Contains("Test error message"));
    }

    [TestMethod]
    public async Task InitializeAllAsync_CompletedEvent_ContainsAllResults()
    {
        // Arrange
        DeferredInitCompletedEventArgs? completedArgs = null;
        _initializer.InitializationCompleted += (s, e) => completedArgs = e;

        _initializer.Register<object>("Service1", () => new object(), _ => { });
        _initializer.Register<object>("FailingService", () => throw new Exception("fail"), _ => { });
        _initializer.Register<object>("Service3", () => new object(), _ => { });

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsNotNull(completedArgs);
        Assert.AreEqual(3, completedArgs.Results.Count);

        var successCount = completedArgs.Results.Count(r => r.Success);
        var failCount = completedArgs.Results.Count(r => !r.Success);

        Assert.AreEqual(2, successCount);
        Assert.AreEqual(1, failCount);
    }

    #endregion

    #region Thread Safety Tests

    [TestMethod]
    public async Task InitializeAllAsync_ConcurrentCalls_OnlyInitializesOnce()
    {
        // Arrange
        var initCount = 0;

        _initializer.RegisterAsync(
            "SlowService",
            async ct =>
            {
                Interlocked.Increment(ref initCount);
                await Task.Delay(50, ct);
            });

        // Act - call concurrently
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _initializer.InitializeAllAsync())
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.AreEqual(1, initCount);
    }

    #endregion

    #region ServicePriority Tests

    [TestMethod]
    public void ServicePriority_Values_AreCorrectlyOrdered()
    {
        // Assert
        Assert.IsTrue(ServicePriority.Critical > ServicePriority.High);
        Assert.IsTrue(ServicePriority.High > ServicePriority.Normal);
        Assert.IsTrue(ServicePriority.Normal > ServicePriority.Low);
    }

    #endregion

    #region Edge Case Tests

    [TestMethod]
    public async Task InitializeAllAsync_SyncFactoryReturnsNull_StillSucceeds()
    {
        // Arrange
        var initializerCalled = false;

        _initializer.Register<object?>(
            "NullFactoryService",
            () => null,
            _ => initializerCalled = true);

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsTrue(initializerCalled);
        Assert.IsTrue(_initializer.IsInitialized);
    }

    [TestMethod]
    public async Task InitializeAllAsync_EmptyServiceName_StillWorks()
    {
        // Arrange
        var initialized = false;

        _initializer.Register<object>(
            "",
            () => new object(),
            _ => initialized = true);

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.IsTrue(initialized);
    }

    [TestMethod]
    public async Task InitializeAllAsync_MultipleServicesWithSamePriority_AllInitialized()
    {
        // Arrange
        var count = 0;

        for (var i = 0; i < 5; i++)
        {
            _initializer.Register<object>(
                $"Service{i}",
                () => new object(),
                _ => Interlocked.Increment(ref count),
                ServicePriority.Normal);
        }

        // Act
        await _initializer.InitializeAllAsync();

        // Assert
        Assert.AreEqual(5, count);
    }

    #endregion
}
