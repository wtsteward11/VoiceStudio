using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for tag management API (/api/tags, /api/tags/categories).
  /// Use instead of IBackendClient for TagManager panel.
  /// </summary>
  public interface ITagManagerClient
  {
    Task<Tag[]?> GetTagsAsync(string? category = null, string? search = null, CancellationToken cancellationToken = default);
    Task<Tag?> CreateTagAsync(string name, string? category, string? color, string? description, CancellationToken cancellationToken = default);
    Task<Tag?> UpdateTagAsync(string tagId, string name, string? category, string? color, string? description, CancellationToken cancellationToken = default);
    Task DeleteTagAsync(string tagId, CancellationToken cancellationToken = default);
    Task<TagManagerViewModel.TagCategoriesResponse?> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task MergeTagsAsync(string sourceTagId, string targetTagId, CancellationToken cancellationToken = default);
  }
}
