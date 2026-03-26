using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for ensemble synthesis API (/api/ensemble).
  /// Use instead of IBackendClient for ensemble synthesis, job listing, and job deletion.
  /// </summary>
  public interface IEnsembleSynthesisClient
  {
    Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Services.EnsembleSynthesisResponse?> CreateSynthesisAsync(VoiceStudio.App.Services.EnsembleSynthesisRequest request, CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Services.EnsembleJobStatus[]?> ListJobsAsync(string? projectId = null, CancellationToken cancellationToken = default);
    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);
  }
}
