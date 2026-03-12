using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Deduplicates identical 429 (rate-limit) toasts per endpoint for a configurable window.
  /// </summary>
  public static class RateLimitToastDedupe
  {
    /// <summary>
    /// Returns true if we should suppress (duplicate within window).
    /// When false, records the show in cache; caller should proceed to display toast.
    /// </summary>
    public static bool ShouldSuppress(
        Dictionary<string, DateTime> cache,
        string message,
        string endpoint,
        int dedupeSeconds,
        DateTime now,
        object lockObj)
    {
      var key = $"{message}|{endpoint}";
      lock (lockObj)
      {
        Prune(cache, dedupeSeconds, now);
        if (cache.TryGetValue(key, out var lastShown) &&
            (now - lastShown).TotalSeconds < dedupeSeconds)
          return true;
        cache[key] = now;
        return false;
      }
    }

    private static void Prune(Dictionary<string, DateTime> cache, int dedupeSeconds, DateTime now)
    {
      var cutoff = now.AddSeconds(-dedupeSeconds);
      var toRemove = new List<string>();
      foreach (var kv in cache)
      {
        if (kv.Value < cutoff)
          toRemove.Add(kv.Key);
      }
      foreach (var k in toRemove)
        cache.Remove(k);
    }
  }
}
