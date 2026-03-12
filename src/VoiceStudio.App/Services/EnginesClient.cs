using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Engines client. Delegates to IBackendClient.GetEnginesAsync (single-flight + 60s TTL per ADR-048).
  /// </summary>
  public sealed class EnginesClient : IEnginesClient
  {
    private readonly IBackendClient _backend;

    public EnginesClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<string>> GetEnginesAsync(CancellationToken ct = default)
      => _backend.GetEnginesAsync(ct);
  }
}
