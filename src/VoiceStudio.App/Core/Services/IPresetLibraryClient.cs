using System.Threading;
using System.Threading.Tasks;
using Preset = VoiceStudio.App.ViewModels.Preset;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for preset library API (/api/presets).
  /// Use instead of IBackendClient for preset CRUD, search, types, categories, and apply.
  /// </summary>
  public interface IPresetLibraryClient
  {
    Task<PresetSearchResult> SearchPresetsAsync(string? query, string? presetType, string? category, CancellationToken ct = default);
    Task<Preset> CreatePresetAsync(PresetCreateRequest request, CancellationToken ct = default);
    Task<Preset> UpdatePresetAsync(string presetId, PresetUpdateRequest request, CancellationToken ct = default);
    Task DeletePresetAsync(string presetId, CancellationToken ct = default);
    Task<PresetApplyResult> ApplyPresetAsync(string presetId, string? targetId, CancellationToken ct = default);
    Task<string[]> GetPresetTypesAsync(CancellationToken ct = default);
    Task<string[]> GetCategoriesAsync(string presetType, CancellationToken ct = default);
  }

  public class PresetSearchResult
  {
    public Preset[] Presets { get; set; } = System.Array.Empty<Preset>();
    public int Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
  }

  public class PresetCreateRequest
  {
    public string Name { get; set; } = string.Empty;
    public string PresetType { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public object? Data { get; set; }
    public string[]? Tags { get; set; }
    public bool IsPublic { get; set; }
  }

  public class PresetUpdateRequest
  {
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public object? Data { get; set; }
    public string[]? Tags { get; set; }
    public bool? IsPublic { get; set; }
  }

  public class PresetApplyResult
  {
    public bool Success { get; set; }
    public string PresetId { get; set; } = string.Empty;
    public string? TargetId { get; set; }
  }
}
