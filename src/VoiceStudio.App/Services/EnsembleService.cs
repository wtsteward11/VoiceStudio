using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Ensemble service. Delegates to IBackendClient.
  /// </summary>
  public sealed class EnsembleService : IEnsembleService
  {
    private readonly IBackendClient _backend;

    public EnsembleService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<MultiEngineEnsembleResponse> CreateMultiEngineEnsembleAsync(MultiEngineEnsembleRequest request, CancellationToken ct = default)
      => _backend.CreateMultiEngineEnsembleAsync(request, ct);

    /// <inheritdoc />
    public Task<MultiEngineEnsembleStatus> GetMultiEngineEnsembleStatusAsync(string jobId, CancellationToken ct = default)
      => _backend.GetMultiEngineEnsembleStatusAsync(jobId, ct);
  }
}
