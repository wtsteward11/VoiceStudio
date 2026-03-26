using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for tag organization API (/api/tags/update).
  /// Use instead of IBackendClient for TagOrganization panel.
  /// Tag data is sourced from IProfilesClient; this client handles tag rename only.
  /// </summary>
  public interface ITagOrganizationClient
  {
    Task UpdateTagAsync(string tagId, string newName, CancellationToken cancellationToken = default);
  }
}
