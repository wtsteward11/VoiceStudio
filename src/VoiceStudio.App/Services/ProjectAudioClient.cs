using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Project audio client. Delegates to IBackendClient for project audio operations.
  /// Applies policy: filename validation (reject invalid chars); dedup guard (return existing when same filename).
  /// List/save consistency: list reflects backend state; save invalidates any local cache.
  /// </summary>
  public sealed class ProjectAudioClient : IProjectAudioClient
  {
    private readonly IBackendClient _backend;

    public ProjectAudioClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    public Task<List<ProjectAudioFile>> ListProjectAudioAsync(string projectId, CancellationToken cancellationToken = default)
      => _backend.ListProjectAudioAsync(projectId, cancellationToken);

    public Task<Stream> GetProjectAudioAsync(string projectId, string filename, CancellationToken cancellationToken = default)
      => _backend.GetProjectAudioAsync(projectId, filename, cancellationToken);

    /// <inheritdoc />
    public async Task<ProjectAudioFile> SaveAudioToProjectAsync(string projectId, string audioId, string? filename = null, CancellationToken cancellationToken = default)
    {
      if (!string.IsNullOrEmpty(filename) && filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
      {
        throw new System.ArgumentException(
          "Filename contains invalid characters. Avoid: " + string.Join(", ", Path.GetInvalidFileNameChars().Take(10)) + "...",
          nameof(filename));
      }

      if (!string.IsNullOrEmpty(filename))
      {
        var existing = await _backend.ListProjectAudioAsync(projectId, cancellationToken).ConfigureAwait(false);
        var match = existing.FirstOrDefault(f => string.Equals(f.Filename, filename, System.StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
          return match;
        }
      }

      return await _backend.SaveAudioToProjectAsync(projectId, audioId, filename, cancellationToken).ConfigureAwait(false);
    }
  }
}
