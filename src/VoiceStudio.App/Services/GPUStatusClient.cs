using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for GPU Status API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class GPUStatusClient : IGPUStatusClient
  {
    private readonly IBackendClient _backend;

    public GPUStatusClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<GPUStatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, GPUStatusResponse>(
        "/api/gpu-status",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }
  }
}
