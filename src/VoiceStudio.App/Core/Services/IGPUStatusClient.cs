using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for GPU Status API (/api/gpu-status).
  /// Use instead of IBackendClient for GPUStatus panel.
  /// </summary>
  public interface IGPUStatusClient
  {
    Task<GPUStatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default);
  }
}
