using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Shared request coordination: single-flight, TTL cache, and invalidation.
  /// Used by BackendClient for profiles, engines, and other stable GET endpoints.
  /// </summary>
  public interface IRequestCoordinator
  {
    /// <summary>
    /// Get cached value or create via factory. Concurrent callers for the same key
    /// coalesce to one factory invocation. Results are cached for the given TTL.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
      string key,
      Func<CancellationToken, Task<T>> factory,
      TimeSpan ttl,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cached value for the key. In-flight tasks complete naturally;
    /// next request will miss cache and recompute.
    /// </summary>
    void Invalidate(string key);

    /// <summary>
    /// Remove all cached values whose keys start with the given prefix.
    /// </summary>
    void InvalidateByPrefix(string prefix);
  }
}
