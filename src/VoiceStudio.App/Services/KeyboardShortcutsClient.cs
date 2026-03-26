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
  /// Client for keyboard shortcuts API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class KeyboardShortcutsClient : IKeyboardShortcutsClient
  {
    private readonly IBackendClient _backend;

    public KeyboardShortcutsClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<KeyboardShortcutsShortcut[]?> GetShortcutsAsync(string? category, string? panelId, CancellationToken cancellationToken = default)
    {
      var query = BuildQuery(category, panelId);
      var url = string.IsNullOrEmpty(query) ? "/api/shortcuts" : $"/api/shortcuts?{query}";
      return _backend.SendRequestAsync<object, KeyboardShortcutsShortcut[]>(url, null, HttpMethod.Get, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KeyboardShortcutsShortcut?> UpdateShortcutAsync(string shortcutId, object request, CancellationToken cancellationToken = default)
    {
      var url = $"/api/shortcuts/{Uri.EscapeDataString(shortcutId)}";
      return _backend.SendRequestAsync<object, KeyboardShortcutsShortcut>(url, request, HttpMethod.Put, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KeyboardShortcutsShortcut?> ResetShortcutAsync(string shortcutId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/shortcuts/{Uri.EscapeDataString(shortcutId)}/reset";
      return _backend.SendRequestAsync<object, KeyboardShortcutsShortcut>(url, null, HttpMethod.Post, cancellationToken);
    }

    /// <inheritdoc />
    public Task ResetAllAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>("/api/shortcuts/reset-all", null, HttpMethod.Post, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ConflictCheckResponse?> CheckConflictAsync(string keyCode, string[] modifiers, string? excludeId, CancellationToken cancellationToken = default)
    {
      var queryParams = new NameValueCollection { { "key_code", keyCode } };
      foreach (var m in modifiers)
        queryParams.Add("modifiers", m);
      if (!string.IsNullOrEmpty(excludeId))
        queryParams.Add("exclude_id", excludeId);
      var qs = string.Join("&", queryParams.AllKeys.Cast<string>().Where(k => k != null).SelectMany(k => queryParams.GetValues(k)?.Select(v => $"{k}={Uri.EscapeDataString(v)}") ?? Array.Empty<string>()));
      var url = $"/api/shortcuts/check-conflict?{qs}";
      return _backend.SendRequestAsync<object, ConflictCheckResponse>(url, null, HttpMethod.Get, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ShortcutCategoriesResponse?> GetCategoriesAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, ShortcutCategoriesResponse>("/api/shortcuts/categories", null, HttpMethod.Get, cancellationToken);

    private static string BuildQuery(string? category, string? panelId)
    {
      var q = new NameValueCollection();
      if (!string.IsNullOrEmpty(category)) q.Add("category", category);
      if (!string.IsNullOrEmpty(panelId)) q.Add("panel_id", panelId);
      return string.Join("&", q.AllKeys.Cast<string>().Where(k => k != null).SelectMany(k => q.GetValues(k)?.Select(v => $"{k}={Uri.EscapeDataString(v)}") ?? Array.Empty<string>()));
    }
  }
}
