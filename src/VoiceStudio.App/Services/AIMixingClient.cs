using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for AI mixing and mastering API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AIMixingClient : IAIMixingClient
  {
    private readonly IBackendClient _backend;

    public AIMixingClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<MixAnalysisResponse?> AnalyzeMixAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var endpoint = $"/api/mix-assistant/mix/analyze?project_id={Uri.EscapeDataString(projectId ?? "")}";
      return _backend.SendRequestAsync<object, MixAnalysisResponse>(endpoint, null, System.Net.Http.HttpMethod.Post);
    }

    /// <inheritdoc />
    public Task<MixApplyResponse?> ApplyMixAsync(MixApplyRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<MixApplyRequest, MixApplyResponse>(
          "/api/mix-assistant/mix/apply",
          request,
          System.Net.Http.HttpMethod.Post);
    }

    /// <inheritdoc />
    public Task<MasteringAnalysisResponse?> AnalyzeMasteringAsync(MasteringAnalysisRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<MasteringAnalysisRequest, MasteringAnalysisResponse>(
          "/api/mix-assistant/master/analyze",
          request,
          System.Net.Http.HttpMethod.Post);
    }

    /// <inheritdoc />
    public Task<MasteringApplyResponse?> ApplyMasteringAsync(MasteringApplyRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<MasteringApplyRequest, MasteringApplyResponse>(
          "/api/mix-assistant/master/apply",
          request,
          System.Net.Http.HttpMethod.Post);
    }
  }
}
