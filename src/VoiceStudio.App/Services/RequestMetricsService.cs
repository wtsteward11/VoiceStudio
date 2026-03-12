using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Records and exposes per-endpoint HTTP request counts (sliding window, per minute).
/// Used for diagnostics and UI smoke proof.
/// </summary>
public interface IRequestMetricsService
{
    /// <summary>
    /// Records a request to the given path. Path is normalized for aggregation.
    /// </summary>
    void RecordRequest(string? path);

    /// <summary>
    /// Returns per-endpoint counts for requests in the last 60 seconds.
    /// </summary>
    IReadOnlyDictionary<string, int> GetCountsPerMinute();

    /// <summary>
    /// Returns a serializable snapshot for proof artifacts (endpoint -> count).
    /// </summary>
    IReadOnlyDictionary<string, int> GetSnapshot();

    /// <summary>
    /// Resets all counters (e.g. for smoke test baseline).
    /// </summary>
    void Reset();
}

/// <summary>
/// HTTP handler that clears degraded mode when a backend request succeeds (2xx).
/// Placed outermost so it sees all responses.
/// </summary>
internal sealed class DegradedModeClearHandler : DelegatingHandler
{
    private readonly GracefulDegradationService? _degradationService;

    public DegradedModeClearHandler(GracefulDegradationService? degradationService, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _degradationService = degradationService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode && _degradationService != null && _degradationService.IsDegradedMode)
            _degradationService.ExitDegradedMode();
        return response;
    }
}

/// <summary>
/// HTTP handler that records request paths to IRequestMetricsService before sending.
/// </summary>
internal sealed class RequestMetricsHandler : DelegatingHandler
{
    private readonly IRequestMetricsService _metrics;

    public RequestMetricsHandler(IRequestMetricsService metrics, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath;
        _metrics.RecordRequest(path);
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Sliding-window per-endpoint request counter (60-second window).
/// </summary>
public sealed class RequestMetricsService : IRequestMetricsService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<double>> _timestampsByPath = new();
    private const double WindowSeconds = 60.0;

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "(empty)";
        if (path.StartsWith("/api/profiles", StringComparison.OrdinalIgnoreCase))
            return "/api/profiles";
        if (path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase))
            return "/api/health";
        if (path.StartsWith("/api/engines", StringComparison.OrdinalIgnoreCase))
            return "/api/engines";
        if (path.StartsWith("/api/audio/meters", StringComparison.OrdinalIgnoreCase))
            return "/api/audio/meters";
        return path;
    }

    public void RecordRequest(string? path)
    {
        var key = NormalizePath(path);
        var unixNow = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        lock (_lock)
        {
            if (!_timestampsByPath.TryGetValue(key, out var list))
            {
                list = new List<double>();
                _timestampsByPath[key] = list;
            }
            list.Add(unixNow);
        }
    }

    public IReadOnlyDictionary<string, int> GetCountsPerMinute()
    {
      return GetSnapshot();
    }

    public IReadOnlyDictionary<string, int> GetSnapshot()
    {
      var unixNow = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
      var cutoff = unixNow - WindowSeconds;

      lock (_lock)
      {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in _timestampsByPath)
        {
          var recent = kv.Value.Count(t => t > cutoff);
          if (recent > 0)
            result[kv.Key] = recent;
        }
        return new ReadOnlyDictionary<string, int>(result);
      }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _timestampsByPath.Clear();
        }
    }
}
