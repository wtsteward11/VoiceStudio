using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for AI mixing and mastering API.
  /// Use instead of IBackendClient for AIMixingMastering panel.
  /// </summary>
  public interface IAIMixingClient
  {
    Task<MixAnalysisResponse?> AnalyzeMixAsync(string projectId, CancellationToken cancellationToken = default);

    Task<MixApplyResponse?> ApplyMixAsync(MixApplyRequest request, CancellationToken cancellationToken = default);

    Task<MasteringAnalysisResponse?> AnalyzeMasteringAsync(MasteringAnalysisRequest request, CancellationToken cancellationToken = default);

    Task<MasteringApplyResponse?> ApplyMasteringAsync(MasteringApplyRequest request, CancellationToken cancellationToken = default);
  }
}
