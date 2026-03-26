using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for API key manager API (/api/api-keys).
  /// Use instead of IBackendClient for API key CRUD and validation.
  /// </summary>
  public interface IAPIKeyManagerClient
  {
    Task<APIKeyResponse[]?> GetKeysAsync(CancellationToken cancellationToken = default);
    Task<APIKeyResponse?> CreateKeyAsync(APIKeyCreateRequest request, CancellationToken cancellationToken = default);
    Task<APIKeyResponse?> UpdateKeyAsync(string keyId, APIKeyUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default);
    Task<APIKeyValidationResult?> ValidateKeyAsync(string keyId, CancellationToken cancellationToken = default);
    Task<string[]?> GetSupportedServicesAsync(CancellationToken cancellationToken = default);
  }
}
