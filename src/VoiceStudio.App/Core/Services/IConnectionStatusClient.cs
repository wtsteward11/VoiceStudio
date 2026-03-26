using VoiceStudio.App.Utilities;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for backend connection status. PR-8: extracted from IBackendClient.
  /// Exposes pipeline IsConnected and CircuitState without HTTP.
  /// </summary>
  public interface IConnectionStatusClient
  {
    bool IsConnected { get; }
    CircuitState CircuitState { get; }
  }
}
