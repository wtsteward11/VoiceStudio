using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/templates.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TemplateLibraryClient : ITemplateLibraryClient
  {
    private readonly IBackendClient _backend;

    public TemplateLibraryClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<TemplateLibraryTemplate[]?> GetTemplatesAsync(string? category, string? search, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string>();
      if (!string.IsNullOrEmpty(category))
        queryParams.Add($"category={System.Uri.EscapeDataString(category)}");
      if (!string.IsNullOrEmpty(search))
        queryParams.Add($"search={System.Uri.EscapeDataString(search)}");
      var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
      return _backend.SendRequestAsync<object, TemplateLibraryTemplate[]>(
        $"/api/templates{queryString}",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<TemplateLibraryTemplate?> CreateTemplateAsync(string name, string? category, string? description, CancellationToken cancellationToken = default)
    {
      var request = new
      {
        name,
        category = category ?? "general",
        description,
        project_data = new { },
        tags = new string[] { },
        is_public = false
      };
      return _backend.SendRequestAsync<object, TemplateLibraryTemplate>(
        "/api/templates",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<TemplateLibraryTemplate?> UpdateTemplateAsync(string id, string name, string category, string? description, IReadOnlyList<string> tags, bool isPublic, CancellationToken cancellationToken = default)
    {
      var request = new
      {
        name,
        category,
        description,
        tags = tags ?? (IReadOnlyList<string>)new string[] { },
        is_public = isPublic
      };
      return _backend.SendRequestAsync<object, TemplateLibraryTemplate>(
        $"/api/templates/{id}",
        request,
        HttpMethod.Put,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        $"/api/templates/{id}",
        null,
        HttpMethod.Delete,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<TemplateApplyResult?> ApplyTemplateAsync(string id, string projectName, CancellationToken cancellationToken = default)
    {
      var request = new { project_name = projectName };
      return _backend.SendRequestAsync<object, TemplateApplyResult>(
        $"/api/templates/{id}/apply",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string[]?> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
      var response = await _backend.SendRequestAsync<object, TemplateCategoriesResponse>(
        "/api/templates/categories/list",
        null,
        HttpMethod.Get,
        cancellationToken).ConfigureAwait(false);
      return response?.Categories;
    }

    private class TemplateCategoriesResponse
    {
      public string[] Categories { get; set; } = System.Array.Empty<string>();
    }
  }
}
