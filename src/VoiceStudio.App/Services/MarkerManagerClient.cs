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
  /// Client for marker management API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class MarkerManagerClient : IMarkerManagerClient
  {
    private readonly IBackendClient _backend;

    public MarkerManagerClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public async Task<Marker[]?> GetMarkersAsync(string? projectId = null, string? category = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new System.Collections.Specialized.NameValueCollection();
      if (!string.IsNullOrEmpty(projectId))
        queryParams.Add("project_id", projectId);
      if (!string.IsNullOrEmpty(category))
        queryParams.Add("category", category);

      var queryString = string.Join("&",
          queryParams.AllKeys.SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
          )
      );

      var url = "/api/markers";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return await _backend.SendRequestAsync<object, Marker[]>(url, null, HttpMethod.Get, cancellationToken);
    }

    public Task<Marker?> CreateMarkerAsync(string projectId, string name, double time, string color, string? category, string? description, CancellationToken cancellationToken = default)
    {
      var request = new { name, time, color, category, description, project_id = projectId };
      return _backend.SendRequestAsync<object, Marker>("/api/markers", request, HttpMethod.Post, cancellationToken);
    }

    public Task<Marker?> UpdateMarkerAsync(string markerId, string name, double time, string color, string? category, string? description, CancellationToken cancellationToken = default)
    {
      var request = new { name, time, color, category, description };
      var url = $"/api/markers/{Uri.EscapeDataString(markerId)}";
      return _backend.SendRequestAsync<object, Marker>(url, request, HttpMethod.Put, cancellationToken);
    }

    public Task DeleteMarkerAsync(string markerId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/markers/{Uri.EscapeDataString(markerId)}";
      return _backend.SendRequestAsync<object, object>(url, null, HttpMethod.Delete, cancellationToken);
    }

    public Task<MarkerManagerViewModel.MarkerCategoriesResponse?> GetCategoriesAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/markers/categories/list?project_id={Uri.EscapeDataString(projectId)}";
      return _backend.SendRequestAsync<object, MarkerManagerViewModel.MarkerCategoriesResponse>(url, null, HttpMethod.Get, cancellationToken);
    }
  }
}
