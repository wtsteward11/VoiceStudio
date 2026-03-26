using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Unit tests for DiagnosticsClient.
/// Verifies delegation to IConnectionStatusClient, IHealthVersionClient, ITelemetryClient. PR-8: no IBackendClient.
/// </summary>
[TestClass]
public class DiagnosticsClientTests
{
    private Mock<IConnectionStatusClient> _mockConnectionStatus = null!;
    private Mock<IHealthVersionClient> _mockHealthVersion = null!;
    private Mock<ITelemetryClient> _mockTelemetry = null!;
    private DiagnosticsClient _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockConnectionStatus = new Mock<IConnectionStatusClient>();
        _mockConnectionStatus.Setup(x => x.IsConnected).Returns(true);
        _mockConnectionStatus.Setup(x => x.CircuitState).Returns(CircuitState.Closed);
        _mockHealthVersion = new Mock<IHealthVersionClient>();
        _mockHealthVersion.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockTelemetry = new Mock<ITelemetryClient>();
        _mockTelemetry.Setup(x => x.GetTelemetryAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Telemetry?)null);
        _mockTelemetry.Setup(x => x.GetTracesAsync(It.IsAny<CancellationToken>())).ReturnsAsync((TraceListResponse?)null);
        _sut = new DiagnosticsClient(_mockConnectionStatus.Object, _mockHealthVersion.Object, _mockTelemetry.Object);
    }

    [TestMethod]
    public void Constructor_WithNullConnectionStatus_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new DiagnosticsClient(null!, _mockHealthVersion.Object, _mockTelemetry.Object));
    }

    [TestMethod]
    public void Constructor_WithNullHealthVersion_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new DiagnosticsClient(_mockConnectionStatus.Object, null!, _mockTelemetry.Object));
    }

    [TestMethod]
    public void Constructor_WithNullTelemetry_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new DiagnosticsClient(_mockConnectionStatus.Object, _mockHealthVersion.Object, null!));
    }

    [TestMethod]
    public void IsConnected_DelegatesToConnectionStatusClient()
    {
        _mockConnectionStatus.Setup(x => x.IsConnected).Returns(true);
        Assert.IsTrue(_sut.IsConnected);
        _mockConnectionStatus.Setup(x => x.IsConnected).Returns(false);
        var sut2 = new DiagnosticsClient(_mockConnectionStatus.Object, _mockHealthVersion.Object, _mockTelemetry.Object);
        Assert.IsFalse(sut2.IsConnected);
    }

    [TestMethod]
    public void GetConnectionStatus_WhenDisconnected_ReturnsOffline()
    {
        _mockConnectionStatus.Setup(x => x.IsConnected).Returns(false);
        var sut2 = new DiagnosticsClient(_mockConnectionStatus.Object, _mockHealthVersion.Object, _mockTelemetry.Object);
        Assert.AreEqual("Offline", sut2.GetConnectionStatus());
    }

    [TestMethod]
    public void GetConnectionStatus_WhenConnected_MapsCircuitStateCorrectly()
    {
        _mockConnectionStatus.Setup(x => x.IsConnected).Returns(true);
        _mockConnectionStatus.Setup(x => x.CircuitState).Returns(CircuitState.Open);
        Assert.AreEqual("Circuit Open (Temporarily Unavailable)", _sut.GetConnectionStatus());
        _mockConnectionStatus.Setup(x => x.CircuitState).Returns(CircuitState.HalfOpen);
        Assert.AreEqual("Testing Connection...", _sut.GetConnectionStatus());
        _mockConnectionStatus.Setup(x => x.CircuitState).Returns(CircuitState.Closed);
        Assert.AreEqual("Connected", _sut.GetConnectionStatus());
    }

    [TestMethod]
    public async Task GetTelemetryAsync_DelegatesToTelemetryClient()
    {
        var telemetry = new Telemetry { VramPct = 42 };
        _mockTelemetry.Setup(x => x.GetTelemetryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(telemetry);

        var result = await _sut.GetTelemetryAsync();

        Assert.IsNotNull(result);
        Assert.AreEqual(42, result.VramPct);
        _mockTelemetry.Verify(x => x.GetTelemetryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetTracesAsync_DelegatesToTelemetryClient()
    {
        var traces = new TraceListResponse { Traces = new List<TraceEntry>() };
        _mockTelemetry.Setup(x => x.GetTracesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(traces);

        var result = await _sut.GetTracesAsync();

        Assert.IsNotNull(result);
        _mockTelemetry.Verify(x => x.GetTracesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CheckHealthAsync_DelegatesToHealthVersionClient()
    {
        _mockHealthVersion.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.CheckHealthAsync();

        Assert.IsTrue(result);
        _mockHealthVersion.Verify(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CheckHealthAsync_WithCancellationToken_PassesTokenToHealthVersionClient()
    {
        var cts = new CancellationTokenSource();
        _mockHealthVersion.Setup(x => x.CheckHealthAsync(cts.Token)).ReturnsAsync(false);

        var result = await _sut.CheckHealthAsync(cts.Token);

        Assert.IsFalse(result);
        _mockHealthVersion.Verify(x => x.CheckHealthAsync(cts.Token), Times.Once);
    }
}
