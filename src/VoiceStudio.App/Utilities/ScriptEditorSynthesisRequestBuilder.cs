using System.Collections.Generic;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Utilities
{
  /// <summary>
  /// Explicit defaults and normalization for script-segment synthesis (Pass 04). Centralizes request shape for tests and UX honesty.
  /// </summary>
  public static class ScriptEditorSynthesisDefaults
  {
    public const string DefaultEngine = "xtts";
    public const string DefaultLanguage = "en";

    /// <summary>Script <see cref="Script.Metadata"/> keys tried in order for language override.</summary>
    public static readonly string[] ScriptLanguageMetadataKeys = { "synthesis_language", "language" };
  }

  /// <summary>
  /// Builds <see cref="VoiceSynthesisRequest"/> for a script segment. Engine repeats last successful generation when <see cref="ScriptSegment.GenerationEngineId"/> is set.
  /// </summary>
  public static class ScriptEditorSynthesisRequestBuilder
  {
    public static VoiceSynthesisRequest Build(
      ScriptSegment segment,
      Dictionary<string, object>? scriptMetadata,
      string trimmedText,
      string profileId)
    {
      var engine = !string.IsNullOrWhiteSpace(segment.GenerationEngineId)
        ? segment.GenerationEngineId.Trim()
        : ScriptEditorSynthesisDefaults.DefaultEngine;

      var language = ResolveLanguage(scriptMetadata);

      return new VoiceSynthesisRequest
      {
        Text = trimmedText,
        ProfileId = profileId,
        Engine = engine,
        Language = language
      };
    }

    private static string ResolveLanguage(Dictionary<string, object>? metadata)
    {
      if (metadata == null)
      {
        return ScriptEditorSynthesisDefaults.DefaultLanguage;
      }

      foreach (var key in ScriptEditorSynthesisDefaults.ScriptLanguageMetadataKeys)
      {
        if (metadata.TryGetValue(key, out var value) && value != null)
        {
          var s = value.ToString();
          if (!string.IsNullOrWhiteSpace(s))
          {
            return s.Trim();
          }
        }
      }

      return ScriptEditorSynthesisDefaults.DefaultLanguage;
    }
  }
}
