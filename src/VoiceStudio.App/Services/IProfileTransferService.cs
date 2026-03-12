using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Handles profile import/export: JSON parsing, bundle serialization, and profile creation from import data.
  /// </summary>
  public interface IProfileTransferService
  {
    /// <summary>
    /// Parses JSON import content into profile import data.
    /// </summary>
    /// <param name="json">JSON string (array of profiles or object with "profiles" array).</param>
    /// <returns>Parsed profiles and optional error message.</returns>
    (IReadOnlyList<ProfileImportData> Profiles, string? Error) ParseImports(string json);

    /// <summary>
    /// Creates profiles from import data via the profiles use case.
    /// </summary>
    Task<IReadOnlyList<VoiceProfile>> CreateProfilesFromImportDataAsync(
        IReadOnlyList<ProfileImportData> importData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds export JSON from profiles.
    /// </summary>
    string BuildExportJson(IEnumerable<VoiceProfile> profiles);

    /// <summary>
    /// Sanitizes a string for use as a filename.
    /// </summary>
    string SanitizeFilename(string? value);
  }

  /// <summary>
  /// Data for a single profile in import/export bundles.
  /// </summary>
  public sealed class ProfileImportData
  {
    public string? Name { get; set; }
    public string? Language { get; set; }
    public string? Emotion { get; set; }
    public List<string>? Tags { get; set; }
  }

  /// <summary>
  /// Export bundle structure.
  /// </summary>
  public sealed class ProfileExportBundle
  {
    public string ExportedAt { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public List<ProfileImportData> Profiles { get; set; } = new();
  }
}
