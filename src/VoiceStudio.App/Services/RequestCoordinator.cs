using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Single-flight + TTL cache + invalidation for shared backend reads.
  /// Thread-safe; one lock for cache and in-flight dictionary.
  /// </summary>
  public sealed class RequestCoordinator : IRequestCoordinator
  {
    private readonly object _lock = new();
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly Dictionary<string, Task<object>> _inFlight = new();

    private sealed record CacheEntry(object Value, DateTime ExpiresAtUtc);

    public async Task<T> GetOrCreateAsync<T>(
      string key,
      Func<CancellationToken, Task<T>> factory,
      TimeSpan ttl,
      CancellationToken cancellationToken = default)
    {
      Task<object>? taskToAwait;

      lock (_lock)
      {
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
        {
          return (T)cached.Value;
        }

        if (_inFlight.TryGetValue(key, out var sharedTask))
        {
          taskToAwait = sharedTask;
        }
        else
        {
          var newTask = RunAndCacheAsync(key, factory, ttl, cancellationToken);
          _inFlight[key] = newTask;
          taskToAwait = newTask;
        }
      }

      return (T)await taskToAwait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Invalidate(string key)
    {
      lock (_lock)
      {
        _cache.Remove(key);
      }
    }

    public void InvalidateByPrefix(string prefix)
    {
      lock (_lock)
      {
        var keys = _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in keys)
        {
          _cache.Remove(key);
        }
      }
    }

    private async Task<object> RunAndCacheAsync<T>(
      string key,
      Func<CancellationToken, Task<T>> factory,
      TimeSpan ttl,
      CancellationToken cancellationToken)
    {
      try
      {
        var value = await factory(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
          _cache[key] = new CacheEntry(value!, DateTime.UtcNow.Add(ttl));
        }

        return value!;
      }
      finally
      {
        lock (_lock)
        {
          _inFlight.Remove(key);
        }
      }
    }
  }
}
