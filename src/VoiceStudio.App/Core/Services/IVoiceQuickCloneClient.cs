using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for voice quick-clone API (/api/voice/clone).
  /// Use instead of IBackendClient for VoiceQuickClone panel.
  /// </summary>
  public interface IVoiceQuickCloneClient
  {
    Task<VoiceCloneResponse> CloneVoiceAsync(
        Stream referenceAudio,
        VoiceCloneRequest request,
        CancellationToken cancellationToken = default);
  }
}
