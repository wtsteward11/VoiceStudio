namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Request/response models for the API key manager API (/api/api-keys).
  /// </summary>
  public class APIKeyCreateRequest
  {
    public string ServiceName { get; set; } = string.Empty;
    public string KeyValue { get; set; } = string.Empty;
    public string? Description { get; set; }
  }

  public class APIKeyUpdateRequest
  {
    public string? KeyValue { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
  }

  public class APIKeyResponse
  {
    public string KeyId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string KeyValueMasked { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? LastUsed { get; set; }
    public bool IsActive { get; set; }
    public int UsageCount { get; set; }
  }

  public class APIKeyValidationResult
  {
    public bool Valid { get; set; }
    public string? Message { get; set; }
    public string? LastUsed { get; set; }
  }
}
