using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for engine discovery. Delegates to IBackendClient with single-flight + TTL per ADR-048.
  /// </summary>
  public interface IEnginesClient
  {
    Task<List<string>> GetEnginesAsync(CancellationToken ct = default);
  }
}
