using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Unit tests for ConnectionStatusClient. PR-8: verifies delegation to pipeline.
/// </summary>
[TestClass]
public class ConnectionStatusClientTests
{
    [TestMethod]
    public void ConnectionStatusClient_DelegatesToPipeline_IsConnectedAndCircuitState()
    {
        var (_, connectionClient) = CreateConnectionStatusClientWithHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        Assert.IsTrue(connectionClient.IsConnected, "IsConnected should reflect pipeline state.");
        Assert.AreEqual(CircuitState.Closed, connectionClient.CircuitState, "CircuitState should delegate to pipeline.");
    }

    [TestMethod]
    public void IBackendClient_DoesNotExposeIsConnected()
    {
        var prop = typeof(IBackendClient).GetProperty("IsConnected");
        Assert.IsNull(prop, "IBackendClient must not expose IsConnected after PR-8 extraction.");
    }

    [TestMethod]
    public void BackendClient_DoesNotExposeIsConnectedOrCircuitState()
    {
        var backendType = typeof(BackendClient);
        Assert.IsNull(backendType.GetProperty("IsConnected"), "BackendClient must not expose IsConnected after PR-8.");
        Assert.IsNull(backendType.GetProperty("CircuitState"), "BackendClient must not expose CircuitState after PR-8.");
    }

    private static (BackendClient BackendClient, IConnectionStatusClient ConnectionStatusClient) CreateConnectionStatusClientWithHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var config = new BackendClientConfig
        {
            BaseUrl = "http://localhost:8000",
            WebSocketUrl = string.Empty,
            RequestTimeout = TimeSpan.FromSeconds(30)
        };
        var handler = new TransportTestHandler(respond);
        var appAssembly = typeof(BackendClient).Assembly;
        var contextType = appAssembly.GetType("VoiceStudio.App.Services.BackendHttpContext")
            ?? throw new InvalidOperationException("BackendHttpContext type not found");
        var context = Activator.CreateInstance(contextType, config, new CorrelationIdProvider(), null, null, handler)
            ?? throw new InvalidOperationException("Failed to create BackendHttpContext");
        var backendCtor = typeof(BackendClient).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { contextType, typeof(BackendClientConfig), typeof(IRequestCoordinator) })
            ?? throw new InvalidOperationException("BackendClient constructor not found");
        var backend = (BackendClient)backendCtor.Invoke(new object[] { context, config, new RequestCoordinator() })!;
        var connType = appAssembly.GetType("VoiceStudio.App.Services.ConnectionStatusClient")
            ?? throw new InvalidOperationException("ConnectionStatusClient type not found");
        var connCtor = connType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, new[] { contextType })
            ?? throw new InvalidOperationException("ConnectionStatusClient constructor not found");
        var conn = connCtor.Invoke(new[] { context })
            ?? throw new InvalidOperationException("Failed to create ConnectionStatusClient");
        return (backend, (IConnectionStatusClient)conn);
    }

    private sealed class TransportTestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;
        private int _sequence;

        public TransportTestHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _sequence);
            return Task.FromResult(_respond(request, n));
        }
    }
}
