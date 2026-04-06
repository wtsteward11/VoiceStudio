using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using SSMLDocument = VoiceStudio.App.ViewModels.SSMLDocument;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for SSML API (/api/ssml).
  /// Use instead of IBackendClient for document CRUD, validate, and preview.
  /// </summary>
  public interface ISSMLClient
  {
    Task<SSMLDocument[]> GetDocumentsAsync(string? projectId, string? profileId, CancellationToken ct = default);
    Task<SSMLDocument> CreateDocumentAsync(SSMLCreateRequest request, CancellationToken ct = default);
    Task<SSMLDocument> UpdateDocumentAsync(string documentId, SSMLUpdateRequest request, CancellationToken ct = default);
    Task DeleteDocumentAsync(string documentId, CancellationToken ct = default);
    Task<SSMLValidateResult> ValidateAsync(string content, string? name, CancellationToken ct = default);
    Task<SSMLPreviewResult> PreviewAsync(string content, string? profileId, string? engine, CancellationToken ct = default);
  }

  public class SSMLCreateRequest
  {
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ProfileId { get; set; }
    public string? ProjectId { get; set; }
  }

  public class SSMLUpdateRequest
  {
    public string? Name { get; set; }
    public string? Content { get; set; }
    public string? ProfileId { get; set; }
  }

  public class SSMLValidateResult
  {
    public bool Valid { get; set; }
    public string[] Errors { get; set; } = System.Array.Empty<string>();
    public string[] Warnings { get; set; } = System.Array.Empty<string>();
  }

  public class SSMLPreviewResult
  {
    [System.Text.Json.Serialization.JsonPropertyName("audio_id")]
    public string AudioId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>GAP-054/064: Present when SSML was detected or transformed on the preview path.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("ssml_handling")]
    public SsmlHandlingDiagnostics? SsmlHandling { get; set; }
  }
}
