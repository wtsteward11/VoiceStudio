using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for voice cloning wizard API (/api/voice/clone/wizard).
  /// Use instead of IBackendClient for wizard operations.
  /// </summary>
  public interface IVoiceCloningWizardClient
  {
    Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default);
    Task<VoiceCloningAudioValidationResponse?> ValidateAudioAsync(VoiceCloningAudioValidationRequest request, CancellationToken cancellationToken = default);
    Task<VoiceCloningAudioUploadResponse?> UploadAudioFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<VoiceCloningWizardStartResponse?> StartWizardAsync(VoiceCloningWizardStartRequest request, CancellationToken cancellationToken = default);
    Task StartWizardProcessAsync(string jobId, CancellationToken cancellationToken = default);
    Task<VoiceCloningWizardStatusResponse?> GetWizardStatusAsync(string jobId, CancellationToken cancellationToken = default);
    Task<VoiceCloningWizardFinalizeResponse?> FinalizeWizardAsync(string jobId, VoiceCloningWizardFinalizeRequest request, CancellationToken cancellationToken = default);
    Task CancelWizardAsync(string jobId, CancellationToken cancellationToken = default);
  }
}
