using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Utilities
{
  /// <summary>
  /// Single resolver for backend HTTP base URL used by local playback (e.g. Script Editor segment play).
  /// Aligns with <see cref="BackendClientConfig"/> default when DI is missing or BaseUrl is unset.
  /// </summary>
  public static class BackendPlaybackBaseUrl
  {
    /// <summary>
    /// Resolves the base URL for API-relative audio playback. Pure for tests when config is passed explicitly.
    /// </summary>
    public static string Resolve(BackendClientConfig? config)
    {
      var url = config?.BaseUrl?.TrimEnd('/');
      if (!string.IsNullOrWhiteSpace(url))
      {
        return url;
      }

      return new BackendClientConfig().BaseUrl.TrimEnd('/');
    }
  }
}
