using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/voice/clone. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class VoiceQuickCloneClient : IVoiceQuickCloneClient
  {
    private readonly IBackendClient _backend;

    public VoiceQuickCloneClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<VoiceCloneResponse> CloneVoiceAsync(
        Stream referenceAudio,
        VoiceCloneRequest request,
        CancellationToken cancellationToken = default)
    {
      return _backend.CloneVoiceAsync(referenceAudio, request, cancellationToken);
    }
  }
}
