using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for engine parameter configuration (/api/engines/configure).
  /// Use instead of IBackendClient for engine parameter tuning panel.
  /// </summary>
  public interface IEngineParameterTuningClient
  {
    /// <summary>
    /// Applies engine parameters to the backend configuration.
    /// </summary>
    Task ConfigureEngineAsync(
      string engineId,
      IReadOnlyDictionary<string, object> parameters,
      CancellationToken cancellationToken = default);
  }
}
