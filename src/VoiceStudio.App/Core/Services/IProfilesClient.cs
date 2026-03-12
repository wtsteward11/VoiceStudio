using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Profiles domain client facade. Provides a focused seam for profile CRUD operations,
  /// delegating to the backend transport. Use this instead of IBackendClient for profile
  /// operations to reduce coupling and enable test isolation.
  /// </summary>
  public interface IProfilesClient
  {
    Task<List<VoiceProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);
    Task<VoiceProfile> GetProfileAsync(string profileId, CancellationToken cancellationToken = default);
    Task<VoiceProfile> CreateProfileAsync(
      string name,
      string language = "en",
      string? emotion = null,
      List<string>? tags = null,
      CancellationToken cancellationToken = default);
    Task<VoiceProfile> UpdateProfileAsync(
      string profileId,
      string? name = null,
      string? language = null,
      string? emotion = null,
      List<string>? tags = null,
      CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the profiles list cache. Call after create/update/delete so the next
    /// GetProfilesAsync refetches from the backend.
    /// </summary>
    void InvalidateProfilesCache();
  }
}
