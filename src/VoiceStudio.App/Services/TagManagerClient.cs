using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for tag management API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TagManagerClient : ITagManagerClient
  {
    private readonly IBackendClient _backend;

    public TagManagerClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public async Task<Tag[]?> GetTagsAsync(string? category = null, string? search = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new System.Collections.Specialized.NameValueCollection();
      if (!string.IsNullOrEmpty(category))
        queryParams.Add("category", category);
      if (!string.IsNullOrEmpty(search))
        queryParams.Add("search", search);

      var queryString = string.Join("&",
          queryParams.AllKeys.SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
          )
      );

      var url = "/api/tags";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return await _backend.SendRequestAsync<object, Tag[]>(url, null, HttpMethod.Get, cancellationToken);
    }

    public Task<Tag?> CreateTagAsync(string name, string? category, string? color, string? description, CancellationToken cancellationToken = default)
    {
      var request = new { name, category, color, description };
      return _backend.SendRequestAsync<object, Tag>("/api/tags", request, HttpMethod.Post, cancellationToken);
    }

    public Task<Tag?> UpdateTagAsync(string tagId, string name, string? category, string? color, string? description, CancellationToken cancellationToken = default)
    {
      var request = new { name, category, color, description };
      var url = $"/api/tags/{Uri.EscapeDataString(tagId)}";
      return _backend.SendRequestAsync<object, Tag>(url, request, HttpMethod.Put, cancellationToken);
    }

    public Task DeleteTagAsync(string tagId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/tags/{Uri.EscapeDataString(tagId)}";
      return _backend.SendRequestAsync<object, object>(url, null, HttpMethod.Delete, cancellationToken);
    }

    public Task<TagManagerViewModel.TagCategoriesResponse?> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
      _backend.SendRequestAsync<object, TagManagerViewModel.TagCategoriesResponse>("/api/tags/categories/list", null, HttpMethod.Get, cancellationToken);

    public Task MergeTagsAsync(string sourceTagId, string targetTagId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/tags/merge?source_tag_id={Uri.EscapeDataString(sourceTagId)}&target_tag_id={Uri.EscapeDataString(targetTagId)}";
      return _backend.SendRequestAsync<object, object>(url, null, HttpMethod.Post, cancellationToken);
    }
  }
}
