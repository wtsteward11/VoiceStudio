using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Implements profile import/export: JSON parsing, bundle serialization, profile creation.
  /// </summary>
  public sealed class ProfileTransferService : IProfileTransferService
  {
    private readonly IProfilesUseCase _profilesUseCase;

    public ProfileTransferService(IProfilesUseCase profilesUseCase)
    {
      _profilesUseCase = profilesUseCase ?? throw new ArgumentNullException(nameof(profilesUseCase));
    }

    public (IReadOnlyList<ProfileImportData> Profiles, string? Error) ParseImports(string json)
    {
      try
      {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
          return (ParseProfileArray(root), null);
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
          if (TryGetProperty(root, "profiles", out var profilesElement) && profilesElement.ValueKind == JsonValueKind.Array)
          {
            return (ParseProfileArray(profilesElement), null);
          }

          var single = ParseProfileObject(root);
          return (single != null ? new List<ProfileImportData> { single } : new List<ProfileImportData>(), null);
        }
      }
      catch (JsonException ex)
      {
        return (new List<ProfileImportData>(), ResourceHelper.FormatString("Profile.ImportParseFailed", ex.Message));
      }

      return (new List<ProfileImportData>(), ResourceHelper.GetString("Profile.ImportParseFailed", "Invalid profile import format."));
    }

    public async Task<IReadOnlyList<VoiceProfile>> CreateProfilesFromImportDataAsync(
        IReadOnlyList<ProfileImportData> importData,
        CancellationToken cancellationToken = default)
    {
      var created = new List<VoiceProfile>();
      foreach (var importProfile in importData)
      {
        var name = importProfile.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
          continue;

        var language = string.IsNullOrWhiteSpace(importProfile.Language) ? "en" : importProfile.Language!.Trim();
        var emotion = string.IsNullOrWhiteSpace(importProfile.Emotion) ? null : importProfile.Emotion!.Trim();
        var tags = importProfile.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag!.Trim()).ToList();

        var profile = await _profilesUseCase.CreateAsync(name, language, emotion, tags, cancellationToken).ConfigureAwait(false);
        if (profile != null)
          created.Add(profile);
      }
      return created;
    }

    public string BuildExportJson(IEnumerable<VoiceProfile> profiles)
    {
      var importDataList = profiles
          .Select(p => new ProfileImportData
          {
            Name = p.Name,
            Language = p.Language,
            Emotion = p.Emotion,
            Tags = p.Tags?.ToList()
          })
          .ToList();

      var bundle = new ProfileExportBundle
      {
        ExportedAt = DateTime.UtcNow.ToString("O"),
        Version = "1.0",
        Profiles = importDataList
      };

      return JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
    }

    public string SanitizeFilename(string? value)
    {
      var name = string.IsNullOrWhiteSpace(value) ? "profile_export" : value!;
      var invalid = System.IO.Path.GetInvalidFileNameChars();
      foreach (var c in invalid)
      {
        name = name.Replace(c, '_');
      }
      return name.Length > 200 ? name[..200] : name;
    }

    private static List<ProfileImportData> ParseProfileArray(JsonElement element)
    {
      var profiles = new List<ProfileImportData>();
      foreach (var item in element.EnumerateArray())
      {
        if (item.ValueKind != JsonValueKind.Object)
          continue;

        var profile = ParseProfileObject(item);
        if (profile != null)
          profiles.Add(profile);
      }
      return profiles;
    }

    private static ProfileImportData? ParseProfileObject(JsonElement element)
    {
      var importData = new ProfileImportData();

      if (TryGetProperty(element, "name", out var nameElement))
        importData.Name = nameElement.GetString();

      if (TryGetProperty(element, "language", out var languageElement))
        importData.Language = languageElement.GetString();

      if (TryGetProperty(element, "emotion", out var emotionElement))
        importData.Emotion = emotionElement.GetString();

      if (TryGetProperty(element, "tags", out var tagsElement))
      {
        if (tagsElement.ValueKind == JsonValueKind.Array)
        {
          importData.Tags = tagsElement.EnumerateArray()
              .Select(tag => tag.GetString())
              .Where(tag => !string.IsNullOrWhiteSpace(tag))
              .Select(tag => tag!.Trim())
              .ToList();
        }
        else if (tagsElement.ValueKind == JsonValueKind.String)
        {
          importData.Tags = ParseTags(tagsElement.GetString());
        }
      }

      return importData;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
      foreach (var property in element.EnumerateObject())
      {
        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
        {
          value = property.Value;
          return true;
        }
      }
      value = default;
      return false;
    }

    private static List<string>? ParseTags(string? tagsText)
    {
      if (string.IsNullOrWhiteSpace(tagsText))
        return null;
      return tagsText
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(tag => !string.IsNullOrWhiteSpace(tag))
          .ToList();
    }
  }
}
