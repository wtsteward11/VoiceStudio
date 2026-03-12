using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Authoritative profiles transport boundary. Owns profile-specific list-key policy,
  /// canonical cache invalidation, and profile transport semantics.
  /// Consumers must use IProfilesClient for profile workflows, not IBackendClient.
  /// </summary>
  public sealed class ProfilesClient : IProfilesClient
  {
    /// <summary>
    /// Canonical cache key for profiles list. Used for single-flight, TTL, and invalidation.
    /// </summary>
    public const string ProfilesListKey = "profiles:list";

    private readonly IBackendClient _backend;
    private readonly IRequestCoordinator _coordinator;

    public ProfilesClient(IBackendClient backend, IRequestCoordinator coordinator)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
      _coordinator = coordinator ?? throw new System.ArgumentNullException(nameof(coordinator));
    }

    public Task<List<VoiceProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
      => _backend.GetProfilesAsync(cancellationToken);

    public Task<VoiceProfile> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
      => _backend.GetProfileAsync(profileId, cancellationToken);

    public async Task<VoiceProfile> CreateProfileAsync(
      string name,
      string language = "en",
      string? emotion = null,
      List<string>? tags = null,
      CancellationToken cancellationToken = default)
    {
      var result = await _backend.CreateProfileAsync(name, language, emotion, tags, cancellationToken).ConfigureAwait(false);
      InvalidateProfilesCache();
      return result;
    }

    public async Task<VoiceProfile> UpdateProfileAsync(
      string profileId,
      string? name = null,
      string? language = null,
      string? emotion = null,
      List<string>? tags = null,
      CancellationToken cancellationToken = default)
    {
      var result = await _backend.UpdateProfileAsync(profileId, name, language, emotion, tags, cancellationToken).ConfigureAwait(false);
      InvalidateProfilesCache();
      return result;
    }

    public async Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
      var result = await _backend.DeleteProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
      if (result)
        InvalidateProfilesCache();
      return result;
    }

    /// <summary>
    /// Invalidates the profiles list cache. Called after create/update/delete so the next
    /// GetProfilesAsync refetches from the backend. ProfilesClient owns this semantics.
    /// </summary>
    public void InvalidateProfilesCache()
    {
      _coordinator.Invalidate(ProfilesListKey);
    }
  }
}
