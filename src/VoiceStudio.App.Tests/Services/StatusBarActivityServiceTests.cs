using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Unit tests for StatusBarActivityService.
/// Tests status management, event notifications, and activity tracking.
/// </summary>
[TestClass]
public class StatusBarActivityServiceTests : TestBase
{
    private Mock<IHealthVersionClient> _mockHealthVersionClient = null!;
    private Mock<OperationQueueService> _mockOperationQueue = null!;
    private StatusBarActivityService _service = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _mockHealthVersionClient = new Mock<IHealthVersionClient>();
        _mockHealthVersionClient.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockOperationQueue = new Mock<OperationQueueService> { CallBase = false };
        _service = new StatusBarActivityService(_mockHealthVersionClient.Object, _mockOperationQueue.Object);
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        _service.StopMonitoring();
        _service = null!;
        _mockHealthVersionClient = null!;
        _mockOperationQueue = null!;
        base.TestCleanup();
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_NullHealthVersionClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => new StatusBarActivityService(null!));
    }

    [TestMethod]
    public void Constructor_NullOperationQueue_Succeeds()
    {
        // Act
        var service = new StatusBarActivityService(_mockHealthVersionClient.Object, null);

        // Assert
        Assert.IsNotNull(service);
    }

    #endregion

    #region Initial State Tests

    [TestMethod]
    public void InitialState_ProcessingStatus_IsIdle()
    {
        // Assert
        Assert.AreEqual(ProcessingStatus.Idle, _service.ProcessingStatus);
    }

    [TestMethod]
    public void InitialState_NetworkStatus_IsConnected()
    {
        // Assert
        Assert.AreEqual(NetworkStatus.Connected, _service.NetworkStatus);
    }

    [TestMethod]
    public void InitialState_EngineStatus_IsReady()
    {
        // Assert
        Assert.AreEqual(EngineStatus.Ready, _service.EngineStatus);
    }

    [TestMethod]
    public void InitialState_ActiveJobCount_IsZero()
    {
        // Assert
        Assert.AreEqual(0, _service.ActiveJobCount);
    }

    [TestMethod]
    public void InitialState_QueuedOperationCount_IsZero()
    {
        // Assert
        Assert.AreEqual(0, _service.QueuedOperationCount);
    }

    #endregion

    #region UpdateProcessingStatus Tests

    [TestMethod]
    public void UpdateProcessingStatus_ChangesStatus()
    {
        // Act
        _service.UpdateProcessingStatus(ProcessingStatus.Processing, 3);

        // Assert
        Assert.AreEqual(ProcessingStatus.Processing, _service.ProcessingStatus);
        Assert.AreEqual(3, _service.ActiveJobCount);
    }

    [TestMethod]
    public void UpdateProcessingStatus_RaisesActivityStatusChangedEvent()
    {
        // Arrange
        var eventRaised = false;
        ProcessingStatus receivedStatus = ProcessingStatus.Idle;
        int receivedJobCount = 0;
        
        _service.ActivityStatusChanged += (s, e) =>
        {
            eventRaised = true;
            receivedStatus = e.ProcessingStatus;
            receivedJobCount = e.ActiveJobCount;
        };

        // Act
        _service.UpdateProcessingStatus(ProcessingStatus.Processing, 5);

        // Assert
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(ProcessingStatus.Processing, receivedStatus);
        Assert.AreEqual(5, receivedJobCount);
    }

    [TestMethod]
    public void UpdateProcessingStatus_SameStatus_DoesNotRaiseEvent()
    {
        // Arrange
        _service.UpdateProcessingStatus(ProcessingStatus.Processing, 3);
        var eventRaised = false;
        _service.ActivityStatusChanged += (s, e) => eventRaised = true;

        // Act - same status and job count
        _service.UpdateProcessingStatus(ProcessingStatus.Processing, 3);

        // Assert
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public void UpdateProcessingStatus_DifferentJobCount_RaisesEvent()
    {
        // Arrange
        _service.UpdateProcessingStatus(ProcessingStatus.Processing, 3);
        var eventRaised = false;
        _service.ActivityStatusChanged += (s, e) => eventRaised = true;

        // Act - same status but different job count
        _service.UpdateProcessingStatus(ProcessingStatus.Processing, 5);

        // Assert
        Assert.IsTrue(eventRaised);
    }

    [TestMethod]
    public void UpdateProcessingStatus_AllValues_WorkCorrectly()
    {
        // Test all enum values
        foreach (ProcessingStatus status in Enum.GetValues(typeof(ProcessingStatus)))
        {
            _service.UpdateProcessingStatus(status, 0);
            Assert.AreEqual(status, _service.ProcessingStatus);
        }
    }

    #endregion

    #region UpdateNetworkStatus Tests

    [TestMethod]
    public void UpdateNetworkStatus_ChangesStatus()
    {
        // Act
        _service.UpdateNetworkStatus(NetworkStatus.Disconnected);

        // Assert
        Assert.AreEqual(NetworkStatus.Disconnected, _service.NetworkStatus);
    }

    [TestMethod]
    public void UpdateNetworkStatus_RaisesActivityStatusChangedEvent()
    {
        // Arrange
        var eventRaised = false;
        NetworkStatus receivedStatus = NetworkStatus.Connected;
        
        _service.ActivityStatusChanged += (s, e) =>
        {
            eventRaised = true;
            receivedStatus = e.NetworkStatus;
        };

        // Act
        _service.UpdateNetworkStatus(NetworkStatus.Reconnecting);

        // Assert
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(NetworkStatus.Reconnecting, receivedStatus);
    }

    [TestMethod]
    public void UpdateNetworkStatus_SameStatus_DoesNotRaiseEvent()
    {
        // Arrange
        _service.UpdateNetworkStatus(NetworkStatus.Disconnected);
        var eventRaised = false;
        _service.ActivityStatusChanged += (s, e) => eventRaised = true;

        // Act
        _service.UpdateNetworkStatus(NetworkStatus.Disconnected);

        // Assert
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public void UpdateNetworkStatus_AllValues_WorkCorrectly()
    {
        foreach (NetworkStatus status in Enum.GetValues(typeof(NetworkStatus)))
        {
            _service.UpdateNetworkStatus(status);
            Assert.AreEqual(status, _service.NetworkStatus);
        }
    }

    #endregion

    #region UpdateEngineStatus Tests

    [TestMethod]
    public void UpdateEngineStatus_ChangesStatus()
    {
        // Act
        _service.UpdateEngineStatus(EngineStatus.Busy);

        // Assert
        Assert.AreEqual(EngineStatus.Busy, _service.EngineStatus);
    }

    [TestMethod]
    public void UpdateEngineStatus_RaisesActivityStatusChangedEvent()
    {
        // Arrange
        var eventRaised = false;
        EngineStatus receivedStatus = EngineStatus.Ready;
        
        _service.ActivityStatusChanged += (s, e) =>
        {
            eventRaised = true;
            receivedStatus = e.EngineStatus;
        };

        // Act
        _service.UpdateEngineStatus(EngineStatus.Starting);

        // Assert
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(EngineStatus.Starting, receivedStatus);
    }

    [TestMethod]
    public void UpdateEngineStatus_SameStatus_DoesNotRaiseEvent()
    {
        // Arrange
        _service.UpdateEngineStatus(EngineStatus.Busy);
        var eventRaised = false;
        _service.ActivityStatusChanged += (s, e) => eventRaised = true;

        // Act
        _service.UpdateEngineStatus(EngineStatus.Busy);

        // Assert
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public void UpdateEngineStatus_AllValues_WorkCorrectly()
    {
        foreach (EngineStatus status in Enum.GetValues(typeof(EngineStatus)))
        {
            _service.UpdateEngineStatus(status);
            Assert.AreEqual(status, _service.EngineStatus);
        }
    }

    #endregion

    #region StartMonitoring / StopMonitoring Tests

    [TestMethod]
    public async Task StartMonitoring_CallsHealthVersionClient_WhenMonitoring()
    {
        // Arrange - mock already set up in TestInitialize
        _mockHealthVersionClient.Invocations.Clear();

        // Act - start monitoring; MonitorLoop calls CheckHealthAsync on first iteration
        _service.StartMonitoring();
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        _service.StopMonitoring();

        // Assert - CheckHealthAsync was invoked by the monitoring loop
        _mockHealthVersionClient.Verify(
            x => x.CheckHealthAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [TestMethod]
    public void StartMonitoring_CalledTwice_DoesNotThrow()
    {
        // Act & Assert - no exception
        _service.StartMonitoring();
        _service.StartMonitoring();
        _service.StopMonitoring();
    }

    [TestMethod]
    public void StopMonitoring_BeforeStart_DoesNotThrow()
    {
        // Act & Assert - no exception
        _service.StopMonitoring();
    }

    #endregion

    #region ActivityStatusChangedEventArgs Tests

    [TestMethod]
    public void ActivityStatusChanged_ContainsAllStatuses()
    {
        // Arrange
        ActivityStatusChangedEventArgs? receivedArgs = null;
        _service.ActivityStatusChanged += (s, e) => receivedArgs = e;

        // Setup various statuses
        _service.UpdateProcessingStatus(ProcessingStatus.Processing, 2);
        _service.UpdateNetworkStatus(NetworkStatus.Reconnecting);
        _service.UpdateEngineStatus(EngineStatus.Starting);

        // Update again to trigger event with all statuses
        _service.UpdateProcessingStatus(ProcessingStatus.Paused, 3);

        // Assert
        Assert.IsNotNull(receivedArgs);
        Assert.AreEqual(ProcessingStatus.Paused, receivedArgs.ProcessingStatus);
        Assert.AreEqual(NetworkStatus.Reconnecting, receivedArgs.NetworkStatus);
        Assert.AreEqual(EngineStatus.Starting, receivedArgs.EngineStatus);
        Assert.AreEqual(3, receivedArgs.ActiveJobCount);
    }

    #endregion

    #region Enum Value Tests

    [TestMethod]
    public void ProcessingStatus_HasExpectedValues()
    {
        // Verify enum values match expected
        Assert.AreEqual(0, (int)ProcessingStatus.Idle);
        Assert.AreEqual(1, (int)ProcessingStatus.Processing);
        Assert.AreEqual(2, (int)ProcessingStatus.Paused);
        Assert.AreEqual(3, (int)ProcessingStatus.Error);
    }

    [TestMethod]
    public void NetworkStatus_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)NetworkStatus.Connected);
        Assert.AreEqual(1, (int)NetworkStatus.Disconnected);
        Assert.AreEqual(2, (int)NetworkStatus.Reconnecting);
        Assert.AreEqual(3, (int)NetworkStatus.Error);
    }

    [TestMethod]
    public void EngineStatus_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)EngineStatus.Ready);
        Assert.AreEqual(1, (int)EngineStatus.Busy);
        Assert.AreEqual(2, (int)EngineStatus.Starting);
        Assert.AreEqual(3, (int)EngineStatus.Offline);
        Assert.AreEqual(4, (int)EngineStatus.Error);
    }

    #endregion

    #region Multiple Status Change Tests

    [TestMethod]
    public void MultipleStatusChanges_TracksCorrectly()
    {
        // Arrange
        var changeCount = 0;
        _service.ActivityStatusChanged += (s, e) => changeCount++;

        // Act
        _service.UpdateProcessingStatus(ProcessingStatus.Processing, 1);
        _service.UpdateNetworkStatus(NetworkStatus.Disconnected);
        _service.UpdateEngineStatus(EngineStatus.Busy);
        _service.UpdateProcessingStatus(ProcessingStatus.Idle, 0);

        // Assert
        Assert.AreEqual(4, changeCount);
        Assert.AreEqual(ProcessingStatus.Idle, _service.ProcessingStatus);
        Assert.AreEqual(NetworkStatus.Disconnected, _service.NetworkStatus);
        Assert.AreEqual(EngineStatus.Busy, _service.EngineStatus);
        Assert.AreEqual(0, _service.ActiveJobCount);
    }

    #endregion
}
