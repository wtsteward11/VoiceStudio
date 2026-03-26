using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for tag organization API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TagOrganizationClient : ITagOrganizationClient
  {
    private readonly IBackendClient _backend;

    public TagOrganizationClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task UpdateTagAsync(string tagId, string newName, CancellationToken cancellationToken = default)
    {
      var request = new { tag_id = tagId, new_name = newName };
      return _backend.SendRequestAsync<object, object>("/api/tags/update", request, cancellationToken);
    }
  }
}
