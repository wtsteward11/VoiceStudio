using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/style-transfer (voice style transfer).
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class VoiceStyleTransferClient : IVoiceStyleTransferClient
  {
    private readonly IBackendClient _backend;

    public VoiceStyleTransferClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<VoiceStyleTransferProfileResponse?> ExtractStyleAsync(
      VoiceStyleTransferExtractRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<VoiceStyleTransferExtractRequest, VoiceStyleTransferProfileResponse>(
        "/api/style-transfer/style/extract",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<VoiceStyleTransferAnalyzeResponse?> AnalyzeStyleAsync(
      VoiceStyleTransferAnalyzeRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<VoiceStyleTransferAnalyzeRequest, VoiceStyleTransferAnalyzeResponse>(
        "/api/style-transfer/style/analyze",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<VoiceStyleTransferSynthesizeResponse?> SynthesizeStyleAsync(
      VoiceStyleTransferSynthesizeRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<VoiceStyleTransferSynthesizeRequest, VoiceStyleTransferSynthesizeResponse>(
        "/api/style-transfer/synthesize/style",
        request,
        HttpMethod.Post,
        cancellationToken);
    }
  }
}
