using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for multi-engine ensemble synthesis. Delegates to IBackendClient.
  /// </summary>
  public interface IEnsembleService
  {
    Task<MultiEngineEnsembleResponse> CreateMultiEngineEnsembleAsync(MultiEngineEnsembleRequest request, CancellationToken ct = default);
    Task<MultiEngineEnsembleStatus> GetMultiEngineEnsembleStatusAsync(string jobId, CancellationToken ct = default);
  }
}
