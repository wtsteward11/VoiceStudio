using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Deterministic, draft-only filler cleanup for the inline transcript editor.
  /// Phrase-first (longest token-count first); single-token cleanup runs only after phrases (GAP-047).
  /// </summary>
  public static class TranscriptFillerCleanupHelper
  {
    public const int MaxInputLength = 200_000;

    /// <summary>Outcome of <see cref="RemoveFillers"/> including per-key removal tallies.</summary>
    public readonly record struct FillerCleanupResult(string CleanedText, IReadOnlyList<RemovalPlanEntry> Plan)
    {
      public int RemovedOccurrenceCount => Plan.Sum(p => p.OccurrenceCount);

      public string TermsSummary => BuildTermsSummary(Plan);
    }

    /// <summary>Risky single-token keys shown in UI but off by default (false positives).</summary>
    public static readonly HashSet<string> RiskySingleTokenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
      "like",
    };

    private static readonly IReadOnlyList<string> PhraseCatalog = new ReadOnlyCollection<string>(new[]
    {
      "you know",
      "i mean",
      "kind of",
      "sort of",
      "i guess",
    });

    private static readonly IReadOnlyList<string> SingleTokenCatalog = new ReadOnlyCollection<string>(new[]
    {
      "um",
      "uh",
      "erm",
      "hmm",
      "like",
    });

    public readonly struct RemovalPlanEntry
    {
      public RemovalPlanEntry(string catalogKey, bool isPhrase, int occurrenceCount, int totalCharLengthRemoved)
      {
        CatalogKey = catalogKey ?? throw new ArgumentNullException(nameof(catalogKey));
        IsPhrase = isPhrase;
        OccurrenceCount = occurrenceCount;
        TotalCharLengthRemoved = totalCharLengthRemoved;
      }

      public string CatalogKey { get; }
      public bool IsPhrase { get; }
      public int OccurrenceCount { get; }
      public int TotalCharLengthRemoved { get; }
    }

    public static bool IsPhraseCatalogKey(string catalogKey)
    {
      return PhraseCatalog.Contains(catalogKey, StringComparer.OrdinalIgnoreCase);
    }

    public static string BuildTermsSummary(IReadOnlyList<RemovalPlanEntry> plan)
    {
      if (plan == null || plan.Count == 0)
      {
        return string.Empty;
      }

      return string.Join(", ", plan.Select(p => $"{p.CatalogKey}×{p.OccurrenceCount}"));
    }

    /// <summary>
    /// Build removal plan for current text. If <paramref name="enabledPhraseKeys"/> / <paramref name="enabledSingleTokenKeys"/> are non-null,
    /// only those catalog keys contribute to counts and preview (subset mode).
    /// </summary>
    public static IReadOnlyList<RemovalPlanEntry> GetRemovalPlan(
        string? draftText,
        IReadOnlySet<string>? enabledPhraseKeys,
        IReadOnlySet<string>? enabledSingleTokenKeys)
    {
      if (draftText is null)
      {
        return Array.Empty<RemovalPlanEntry>();
      }

      if (draftText.Length > MaxInputLength)
      {
        throw new ArgumentOutOfRangeException(nameof(draftText), draftText.Length, $"draft exceeds {MaxInputLength} characters.");
      }

      var result = new List<RemovalPlanEntry>();
      if (draftText.Length == 0)
      {
        return result;
      }

      var sortedPhrases = SortPhrasesLongestFirst(PhraseCatalog);
      if (enabledPhraseKeys != null)
      {
        sortedPhrases = sortedPhrases.Where(p => enabledPhraseKeys.Contains(p)).ToList();
      }

      var working = draftText;
      foreach (var phrase in sortedPhrases)
      {
        var (stripped, count, lenRemoved) = StripWholeWordPhraseOccurrences(working, phrase);
        if (count > 0)
        {
          result.Add(new RemovalPlanEntry(phrase, isPhrase: true, occurrenceCount: count, totalCharLengthRemoved: lenRemoved));
          working = stripped;
        }
      }

      var sortedTokens = SingleTokenCatalog
          .OrderByDescending(t => t.Length)
          .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
          .ToList();
      if (enabledSingleTokenKeys != null)
      {
        sortedTokens = sortedTokens.Where(t => enabledSingleTokenKeys.Contains(t)).ToList();
      }

      foreach (var token in sortedTokens)
      {
        var (stripped, count, lenRemoved) = StripWholeWordSingleTokenOccurrences(working, token);
        if (count > 0)
        {
          result.Add(new RemovalPlanEntry(token, isPhrase: false, occurrenceCount: count, totalCharLengthRemoved: lenRemoved));
          working = stripped;
        }
      }

      return result;
    }

    /// <summary>Preview text after phrase + single-token cleanup using enabled sets only.</summary>
    public static string GetPreviewAfterRemoval(
        string draftText,
        IReadOnlySet<string> enabledPhraseKeys,
        IReadOnlySet<string> enabledSingleTokenKeys)
    {
      if (draftText is null)
      {
        throw new ArgumentNullException(nameof(draftText));
      }

      if (enabledPhraseKeys is null)
      {
        throw new ArgumentNullException(nameof(enabledPhraseKeys));
      }

      if (enabledSingleTokenKeys is null)
      {
        throw new ArgumentNullException(nameof(enabledSingleTokenKeys));
      }

      var (text, _) = RemoveFillersImpl(draftText, enabledPhraseKeys, enabledSingleTokenKeys);
      return text;
    }

    /// <summary>
    /// Remove fillers (whole-word / phrase-boundary). When enabled sets are null, all catalog entries apply (backward compatible).
    /// </summary>
    public static FillerCleanupResult RemoveFillers(
        string? draftText,
        IReadOnlySet<string>? enabledPhraseKeys = null,
        IReadOnlySet<string>? enabledSingleTokenKeys = null)
    {
      if (draftText is null)
      {
        return new FillerCleanupResult(string.Empty, Array.Empty<RemovalPlanEntry>());
      }

      var (text, plan) = RemoveFillersImpl(draftText, enabledPhraseKeys, enabledSingleTokenKeys);
      return new FillerCleanupResult(text, plan);
    }

    public static FillerCleanupResult RemoveFillersWithPlan(
        string? draftText,
        IReadOnlySet<string>? enabledPhraseKeys = null,
        IReadOnlySet<string>? enabledSingleTokenKeys = null)
    {
      return RemoveFillers(draftText, enabledPhraseKeys, enabledSingleTokenKeys);
    }

    private static (string CleanedText, IReadOnlyList<RemovalPlanEntry> Plan) RemoveFillersImpl(
        string draftText,
        IReadOnlySet<string>? enabledPhraseKeys,
        IReadOnlySet<string>? enabledSingleTokenKeys)
    {
      if (draftText is null)
      {
        throw new ArgumentNullException(nameof(draftText));
      }

      if (draftText.Length > MaxInputLength)
      {
        throw new ArgumentOutOfRangeException(nameof(draftText), draftText.Length, $"draft exceeds {MaxInputLength} characters.");
      }

      if (draftText.Length == 0)
      {
        return (string.Empty, Array.Empty<RemovalPlanEntry>());
      }

      var plan = new List<RemovalPlanEntry>();
      var working = draftText;

      var sortedPhrases = SortPhrasesLongestFirst(PhraseCatalog);
      if (enabledPhraseKeys != null)
      {
        sortedPhrases = sortedPhrases.Where(p => enabledPhraseKeys.Contains(p)).ToList();
      }

      foreach (var phrase in sortedPhrases)
      {
        var (stripped, count, lenRemoved) = StripWholeWordPhraseOccurrences(working, phrase);
        if (count > 0)
        {
          plan.Add(new RemovalPlanEntry(phrase, isPhrase: true, occurrenceCount: count, totalCharLengthRemoved: lenRemoved));
          working = stripped;
        }
      }

      var sortedTokens = SingleTokenCatalog
          .OrderByDescending(t => t.Length)
          .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
              .ToList();
      if (enabledSingleTokenKeys != null)
      {
        sortedTokens = sortedTokens.Where(t => enabledSingleTokenKeys.Contains(t)).ToList();
      }

      foreach (var token in sortedTokens)
      {
        var (stripped, count, lenRemoved) = StripWholeWordSingleTokenOccurrences(working, token);
        if (count > 0)
        {
          plan.Add(new RemovalPlanEntry(token, isPhrase: false, occurrenceCount: count, totalCharLengthRemoved: lenRemoved));
          working = stripped;
        }
      }

      working = CollapseRepeatedWhitespace(working).Trim();
      return (working, plan);
    }

    private static List<string> SortPhrasesLongestFirst(IEnumerable<string> phrases)
    {
      return phrases
          .Select(p => p.Trim())
          .Select(p => p.ToLower(CultureInfo.InvariantCulture))
          .Where(p => p.Length > 0)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .OrderByDescending(p => TokenCount(p))
          .ThenByDescending(p => p.Length)
          .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
          .ToList();
    }

    private static int TokenCount(string phraseLowerInvariant)
    {
      var count = 0;
      foreach (var _ in phraseLowerInvariant.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      {
        count++;
      }

      return count;
    }

    private static (string Text, int Occurrences, int TotalCharsRemoved) StripWholeWordPhraseOccurrences(string input, string phraseLowerInvariant)
    {
      if (string.IsNullOrWhiteSpace(phraseLowerInvariant))
      {
        return (input, 0, 0);
      }

      var phraseWords = phraseLowerInvariant.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (phraseWords.Length == 0)
      {
        return (input, 0, 0);
      }

      var pattern = BuildPhrasePattern(phraseWords);
      var totalRemoved = 0;
      var occurrences = 0;
      var result = Regex.Replace(
          input,
          pattern,
          match =>
          {
            totalRemoved += match.Value.Length;
            occurrences++;
            return string.Empty;
          },
          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

      result = CollapseRepeatedWhitespace(result);
      return (result, occurrences, totalRemoved);
    }

    private static string BuildPhrasePattern(string[] phraseWordsLowerInvariant)
    {
      Debug.Assert(phraseWordsLowerInvariant.Length > 0, "phraseWordsLowerInvariant should not be empty.");
      var sb = new StringBuilder();
      sb.Append(@"(?<![\p{L}\p{Nd}_'])");
      sb.Append(@"(?:");
      for (var i = 0; i < phraseWordsLowerInvariant.Length; i++)
      {
        if (i > 0)
        {
          sb.Append(@"\s+");
        }

        sb.Append(Regex.Escape(phraseWordsLowerInvariant[i]));
      }

      sb.Append(@")");
      sb.Append(@"(?:[.,!?]+)?");
      sb.Append(@"(?![\p{L}\p{Nd}_'])");
      return sb.ToString();
    }

    private static (string Text, int Occurrences, int TotalCharsRemoved) StripWholeWordSingleTokenOccurrences(string input, string tokenLowerInvariant)
    {
      if (string.IsNullOrWhiteSpace(tokenLowerInvariant))
      {
        return (input, 0, 0);
      }

      var escaped = Regex.Escape(tokenLowerInvariant);
      var pattern = $@"(?<![\p{{L}}\p{{Nd}}_'])(?:{escaped})(?:[.,!?]+)?(?![\p{{L}}\p{{Nd}}_'])";
      var totalRemoved = 0;
      var occurrences = 0;
      var result = Regex.Replace(
          input,
          pattern,
          match =>
          {
            totalRemoved += match.Value.Length;
            occurrences++;
            return string.Empty;
          },
          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

      result = CollapseRepeatedWhitespace(result);
      return (result, occurrences, totalRemoved);
    }

    private static string CollapseRepeatedWhitespace(string input)
    {
      if (string.IsNullOrEmpty(input))
      {
        return input;
      }

      return Regex.Replace(input, @"\s{2,}", " ", RegexOptions.CultureInvariant);
    }
  }
}
