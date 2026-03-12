using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.UseCases
{
  /// <summary>
  /// Use case implementation that delegates profile operations to IProfilesClient.
  /// ListAsync benefits from ProfilesClient's built-in single-flight + TTL caching for GetProfilesAsync.
  /// </summary>
  public sealed class ProfilesUseCase : IProfilesUseCase
  {
    private readonly IProfilesClient _profilesClient;

    public ProfilesUseCase(IProfilesClient profilesClient)
    {
      _profilesClient = profilesClient ?? throw new System.ArgumentNullException(nameof(profilesClient));
    }

    public async Task<IReadOnlyList<VoiceProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
      var list = await _profilesClient.GetProfilesAsync(cancellationToken).ConfigureAwait(false);
      return list ?? new List<VoiceProfile>();
    }

    public Task<VoiceProfile> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
      return _profilesClient.CreateProfileAsync(name, "en", null, null, cancellationToken);
    }

    public Task<VoiceProfile> CreateAsync(string name, string? language, string? emotion, List<string>? tags, CancellationToken cancellationToken = default)
    {
      return _profilesClient.CreateProfileAsync(name, language ?? "en", emotion, tags, cancellationToken);
    }

    public Task<VoiceProfile> UpdateAsync(string profileId, string? name, string? language, string? emotion, List<string>? tags, CancellationToken cancellationToken = default)
    {
      return _profilesClient.UpdateProfileAsync(profileId, name, language, emotion, tags, cancellationToken);
    }

    public Task<bool> DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
      return _profilesClient.DeleteProfileAsync(profileId, cancellationToken);
    }
  }
}