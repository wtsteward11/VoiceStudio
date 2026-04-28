using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceStudio.App.Core.Models;

/// <summary>
/// Shared parsing and mutation for the optional <c>vs:engines:id1,id2</c> profile tag convention.
/// Semantics: first tag with prefix <c>vs:engines:</c> (ordinal); payload split on comma; engine id match is ordinal-ignore-case.
/// See <c>docs/reports/verification/VOICE_SYNTHESIS_PROFILE_ENGINE_COMPATIBILITY_UX_2026-04-28.md</c>.
/// </summary>
internal static class VoiceProfileEngineCompatibilityTags
{
  public const string TagPrefix = "vs:engines:";

  /// <summary>Returns true when a non-empty allow-list was parsed from tags.</summary>
  public static bool TryParseAllowedEngines(IReadOnlyList<string>? tags, out HashSet<string>? allowList)
  {
    allowList = null;
    if (tags is null || tags.Count == 0)
      return false;

    foreach (var raw in tags)
    {
      if (string.IsNullOrWhiteSpace(raw))
        continue;
      if (!raw.StartsWith(TagPrefix, StringComparison.Ordinal))
        continue;

      var payload = raw.Length > TagPrefix.Length
          ? raw.Substring(TagPrefix.Length)
          : string.Empty;
      var parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (parts.Length == 0)
        return false;

      allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var p in parts)
      {
        if (!string.IsNullOrWhiteSpace(p))
          allowList.Add(p.Trim());
      }

      return allowList.Count > 0;
    }

    return false;
  }

  public static IReadOnlyList<string> ParseAllowedEngineIds(IReadOnlyList<string>? tags)
  {
    if (tags is null || tags.Count == 0)
      return Array.Empty<string>();

    foreach (var raw in tags)
    {
      if (string.IsNullOrWhiteSpace(raw))
        continue;
      if (!raw.StartsWith(TagPrefix, StringComparison.Ordinal))
        continue;

      var payload = raw.Length > TagPrefix.Length
          ? raw.Substring(TagPrefix.Length)
          : string.Empty;
      var parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (parts.Length == 0)
        return Array.Empty<string>();

      var comparer = StringComparer.OrdinalIgnoreCase;
      var seen = new HashSet<string>(comparer);
      var ordered = new List<string>();
      foreach (var p in parts)
      {
        var t = p.Trim();
        if (string.IsNullOrWhiteSpace(t) || !seen.Add(t))
          continue;
        ordered.Add(t);
      }

      if (ordered.Count == 0)
        return Array.Empty<string>();

      ordered.Sort(StringComparer.OrdinalIgnoreCase);
      return ordered;
    }

    return Array.Empty<string>();
  }

  public static string BuildEnginesTag(IEnumerable<string> engineIds)
  {
    var normalized = NormalizeEngineIds(engineIds);
    return TagPrefix + string.Join(",", normalized);
  }

  public static List<string> ReplaceEnginesTag(IReadOnlyList<string>? currentTags, IReadOnlyList<string> engineIds)
  {
    var withoutEngines = new List<string>();
    if (currentTags != null)
    {
      foreach (var t in currentTags)
      {
        if (string.IsNullOrWhiteSpace(t))
          continue;
        if (t.StartsWith(TagPrefix, StringComparison.Ordinal))
          continue;
        withoutEngines.Add(t);
      }
    }

    var normalized = NormalizeEngineIds(engineIds);
    if (normalized.Count == 0)
      return withoutEngines;

    var result = new List<string> { BuildEnginesTag(normalized) };
    result.AddRange(withoutEngines);
    return result;
  }

  private static List<string> NormalizeEngineIds(IEnumerable<string> engineIds)
  {
    var comparer = StringComparer.OrdinalIgnoreCase;
    var seen = new HashSet<string>(comparer);
    var firstSeen = new Dictionary<string, string>(comparer);
    foreach (var raw in engineIds)
    {
      if (string.IsNullOrWhiteSpace(raw))
        continue;
      var trimmed = raw.Trim();
      if (!seen.Add(trimmed))
        continue;
      firstSeen[trimmed] = trimmed;
    }

    var list = firstSeen.Values.ToList();
    list.Sort(StringComparer.OrdinalIgnoreCase);
    return list;
  }
}
