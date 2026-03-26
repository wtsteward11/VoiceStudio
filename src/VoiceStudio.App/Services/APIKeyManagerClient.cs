using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/api-keys. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class APIKeyManagerClient : IAPIKeyManagerClient
  {
    private readonly IBackendClient _backend;

    public APIKeyManagerClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<APIKeyResponse[]?> GetKeysAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, APIKeyResponse[]>("/api/api-keys", null, HttpMethod.Get, cancellationToken);

    /// <inheritdoc />
    public Task<APIKeyResponse?> CreateKeyAsync(APIKeyCreateRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<APIKeyCreateRequest, APIKeyResponse>("/api/api-keys", request, HttpMethod.Post, cancellationToken);

    /// <inheritdoc />
    public Task<APIKeyResponse?> UpdateKeyAsync(string keyId, APIKeyUpdateRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<APIKeyUpdateRequest, APIKeyResponse>(
          $"/api/api-keys/{Uri.EscapeDataString(keyId)}",
          request,
          HttpMethod.Put,
          cancellationToken);

    /// <inheritdoc />
    public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/api-keys/{Uri.EscapeDataString(keyId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);

    /// <inheritdoc />
    public Task<APIKeyValidationResult?> ValidateKeyAsync(string keyId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, APIKeyValidationResult>(
          $"/api/api-keys/{Uri.EscapeDataString(keyId)}/validate",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<string[]?> GetSupportedServicesAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, string[]>("/api/api-keys/services/list", null, HttpMethod.Get, cancellationToken);
  }
}
