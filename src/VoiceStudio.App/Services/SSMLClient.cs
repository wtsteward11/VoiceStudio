using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using SSMLDocument = VoiceStudio.App.ViewModels.SSMLDocument;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/ssml. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class SSMLClient : ISSMLClient
  {
    private readonly IBackendClient _backend;

    public SSMLClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<SSMLDocument[]> GetDocumentsAsync(string? projectId, string? profileId, CancellationToken ct = default)
    {
      var queryParams = new NameValueCollection();
      if (!string.IsNullOrEmpty(projectId))
        queryParams.Add("project_id", projectId);
      if (!string.IsNullOrEmpty(profileId))
        queryParams.Add("profile_id", profileId);

      var queryString = string.Join("&",
          queryParams.AllKeys.Cast<string>().SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
          )
      );

      var url = "/api/ssml";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      var result = await _backend.SendRequestAsync<object, SSMLDocument[]>(
          url,
          null,
          HttpMethod.Get,
          ct).ConfigureAwait(false);
      return result ?? Array.Empty<SSMLDocument>();
    }

    /// <inheritdoc />
    public async Task<SSMLDocument> CreateDocumentAsync(SSMLCreateRequest request, CancellationToken ct = default)
    {
      var body = new
      {
        name = request.Name,
        content = request.Content,
        profile_id = request.ProfileId,
        project_id = request.ProjectId
      };

      var result = await _backend.SendRequestAsync<object, SSMLDocument>(
          "/api/ssml",
          body,
          HttpMethod.Post,
          ct).ConfigureAwait(false);
      return result ?? throw new InvalidOperationException("Create SSML document returned null");
    }

    /// <inheritdoc />
    public async Task<SSMLDocument> UpdateDocumentAsync(string documentId, SSMLUpdateRequest request, CancellationToken ct = default)
    {
      var body = new
      {
        name = request.Name,
        content = request.Content,
        profile_id = request.ProfileId
      };

      var result = await _backend.SendRequestAsync<object, SSMLDocument>(
          $"/api/ssml/{Uri.EscapeDataString(documentId)}",
          body,
          HttpMethod.Put,
          ct).ConfigureAwait(false);
      return result ?? throw new InvalidOperationException("Update SSML document returned null");
    }

    /// <inheritdoc />
    public Task DeleteDocumentAsync(string documentId, CancellationToken ct = default)
    {
      return _backend.SendRequestAsync<object, object>(
          $"/api/ssml/{Uri.EscapeDataString(documentId)}",
          null,
          HttpMethod.Delete,
          ct);
    }

    /// <inheritdoc />
    public async Task<SSMLValidateResult> ValidateAsync(string content, string? name, CancellationToken ct = default)
    {
      var body = new { name = name ?? "Validation", content };
      var result = await _backend.SendRequestAsync<object, SSMLValidateResult>(
          "/api/ssml/validate",
          body,
          HttpMethod.Post,
          ct).ConfigureAwait(false);
      return result ?? new SSMLValidateResult();
    }

    /// <inheritdoc />
    public async Task<SSMLPreviewResult> PreviewAsync(string content, string? profileId, string? engine, CancellationToken ct = default)
    {
      var body = new { content, profile_id = profileId, engine };
      var result = await _backend.SendRequestAsync<object, SSMLPreviewResult>(
          "/api/ssml/preview",
          body,
          HttpMethod.Post,
          ct).ConfigureAwait(false);
      return result ?? throw new InvalidOperationException("SSML preview returned null");
    }
  }
}
