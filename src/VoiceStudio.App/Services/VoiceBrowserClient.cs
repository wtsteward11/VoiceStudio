using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/voice-browser. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class VoiceBrowserClient : IVoiceBrowserClient
  {
    private readonly IBackendClient _backend;

    public VoiceBrowserClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<VoiceSearchResponse?> SearchVoicesAsync(
      string? query = null,
      string? language = null,
      string? gender = null,
      double minQualityScore = 0,
      string[]? tags = null,
      int limit = 50,
      int offset = 0,
      CancellationToken cancellationToken = default)
    {
      var queryParams = new NameValueCollection();
      if (!string.IsNullOrWhiteSpace(query))
        queryParams.Add("query", query);
      if (!string.IsNullOrEmpty(language))
        queryParams.Add("language", language);
      if (!string.IsNullOrEmpty(gender))
        queryParams.Add("gender", gender);
      if (minQualityScore > 0.0)
        queryParams.Add("min_quality_score", minQualityScore.ToString());
      if (tags != null && tags.Length > 0)
        queryParams.Add("tags", string.Join(",", tags));
      queryParams.Add("limit", limit.ToString());
      queryParams.Add("offset", offset.ToString());

      var queryString = string.Join("&",
          queryParams.AllKeys.SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
          )
      );

      var url = "/api/voice-browser/voices";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return await _backend.SendRequestAsync<object, VoiceSearchResponse>(url, null, HttpMethod.Get, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LanguagesResponse?> GetLanguagesAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, LanguagesResponse>(
          "/api/voice-browser/languages",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<TagsResponse?> GetTagsAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, TagsResponse>(
          "/api/voice-browser/tags",
          null,
          HttpMethod.Get,
          cancellationToken);
  }
}
