using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for backend connection status. PR-8: exposes pipeline state.
  /// Takes BackendHttpContext; delegates to Pipeline.IsConnected and Pipeline.CircuitState.
  /// No HTTP — same pattern as HealthVersionClient for pipeline access.
  /// </summary>
  internal sealed class ConnectionStatusClient : IConnectionStatusClient
  {
    private readonly BackendHttpContext _context;

    internal ConnectionStatusClient(BackendHttpContext httpContext)
    {
      _context = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
    }

    public bool IsConnected => _context.Pipeline.IsConnected;

    public CircuitState CircuitState => _context.Pipeline.CircuitState;
  }
}
