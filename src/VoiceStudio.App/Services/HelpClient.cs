using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for help system API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class HelpClient : IHelpClient
  {
    private readonly IBackendClient _backend;

    public HelpClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<HelpTopic[]?> GetTopicsAsync(string? category, string? panelId, CancellationToken cancellationToken = default)
    {
      var query = BuildQuery(category, panelId);
      var url = string.IsNullOrEmpty(query) ? "/api/help/topics" : $"/api/help/topics?{query}";
      return _backend.SendRequestAsync<object, HelpTopic[]>(url, null, HttpMethod.Get, cancellationToken);
    }

    /// <inheritdoc />
    public Task<HelpSearchResponse?> SearchAsync(string query, string? category, string? panelId, CancellationToken cancellationToken = default)
    {
      var queryParams = new NameValueCollection { { "query", query } };
      if (!string.IsNullOrEmpty(category)) queryParams.Add("category", category);
      if (!string.IsNullOrEmpty(panelId)) queryParams.Add("panel_id", panelId);
      var qs = BuildQueryString(queryParams);
      var url = $"/api/help/search?{qs}";
      return _backend.SendRequestAsync<object, HelpSearchResponse>(url, null, HttpMethod.Get, cancellationToken);
    }

    /// <inheritdoc />
    public Task<HelpKeyboardShortcut[]?> GetShortcutsAsync(string? panelId, CancellationToken cancellationToken = default)
    {
      var query = string.IsNullOrEmpty(panelId) ? "" : $"panel_id={Uri.EscapeDataString(panelId)}";
      var url = string.IsNullOrEmpty(query) ? "/api/help/shortcuts" : $"/api/help/shortcuts?{query}";
      return _backend.SendRequestAsync<object, HelpKeyboardShortcut[]>(url, null, HttpMethod.Get, cancellationToken);
    }

    /// <inheritdoc />
    public Task<HelpCategoriesResponse?> GetCategoriesAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, HelpCategoriesResponse>("/api/help/categories", null, HttpMethod.Get, cancellationToken);

    /// <inheritdoc />
    public Task<PanelHelpResponse?> GetPanelHelpAsync(string panelId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/help/panel/{Uri.EscapeDataString(panelId)}";
      return _backend.SendRequestAsync<object, PanelHelpResponse>(url, null, HttpMethod.Get, cancellationToken);
    }

    private static string BuildQuery(string? category, string? panelId)
    {
      var queryParams = new NameValueCollection();
      if (!string.IsNullOrEmpty(category)) queryParams.Add("category", category);
      if (!string.IsNullOrEmpty(panelId)) queryParams.Add("panel_id", panelId);
      return BuildQueryString(queryParams);
    }

    private static string BuildQueryString(NameValueCollection queryParams) =>
      string.Join("&",
        queryParams.AllKeys
          .Cast<string>()
          .Where(k => k != null)
          .SelectMany(key => queryParams.GetValues(key)?.Select(v => $"{key}={Uri.EscapeDataString(v)}") ?? Array.Empty<string>()));
  }
}
