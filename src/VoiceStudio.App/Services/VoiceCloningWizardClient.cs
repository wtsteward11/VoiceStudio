using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/voice/clone/wizard. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class VoiceCloningWizardClient : IVoiceCloningWizardClient
  {
    private readonly IBackendClient _backend;

    public VoiceCloningWizardClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default)
      => _backend.GetEnginesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<VoiceCloningAudioValidationResponse?> ValidateAudioAsync(VoiceCloningAudioValidationRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<VoiceCloningAudioValidationRequest, VoiceCloningAudioValidationResponse>(
          "/api/voice/clone/wizard/validate-audio",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<VoiceCloningAudioUploadResponse?> UploadAudioFileAsync(string filePath, CancellationToken cancellationToken = default)
      => _backend.UploadFileWithProgressAsync<VoiceCloningAudioUploadResponse>(
          "/api/audio/upload",
          filePath,
          "file",
          additionalData: null,
          progress: null,
          timeout: null,
          cancellationToken);

    /// <inheritdoc />
    public Task<VoiceCloningWizardStartResponse?> StartWizardAsync(VoiceCloningWizardStartRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<VoiceCloningWizardStartRequest, VoiceCloningWizardStartResponse>(
          "/api/voice/clone/wizard/start",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task StartWizardProcessAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/voice/clone/wizard/{Uri.EscapeDataString(jobId)}/process",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<VoiceCloningWizardStatusResponse?> GetWizardStatusAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, VoiceCloningWizardStatusResponse>(
          $"/api/voice/clone/wizard/{Uri.EscapeDataString(jobId)}/status",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<VoiceCloningWizardFinalizeResponse?> FinalizeWizardAsync(string jobId, VoiceCloningWizardFinalizeRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<VoiceCloningWizardFinalizeRequest, VoiceCloningWizardFinalizeResponse>(
          $"/api/voice/clone/wizard/{Uri.EscapeDataString(jobId)}/finalize",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task CancelWizardAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/voice/clone/wizard/{Uri.EscapeDataString(jobId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);
  }
}
