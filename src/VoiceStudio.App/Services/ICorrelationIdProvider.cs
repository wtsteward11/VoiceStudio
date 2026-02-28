// Copyright (c) VoiceStudio. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// GAP-I12: Provides correlation IDs for linking frontend logs to backend requests.
  /// Thread-safe implementation using AsyncLocal for async flow.
  /// </summary>
  public interface ICorrelationIdProvider
  {
    /// <summary>
    /// Gets the current correlation ID for the current async context.
    /// </summary>
    string? GetCurrentCorrelationId();

    /// <summary>
    /// Gets the current trace ID for distributed tracing.
    /// </summary>
    string? GetCurrentTraceId();

    /// <summary>
    /// Gets the current span ID for distributed tracing.
    /// </summary>
    string? GetCurrentSpanId();

    /// <summary>
    /// Sets the correlation ID for the current async context.
    /// Called when receiving HTTP response headers from backend.
    /// </summary>
    void SetCorrelationId(string correlationId);

    /// <summary>
    /// Sets trace context from backend response headers.
    /// </summary>
    void SetTraceContext(string? traceId, string? spanId);

    /// <summary>
    /// Clears the current correlation context.
    /// </summary>
    void ClearContext();

    /// <summary>
    /// Creates a scoped correlation context that auto-clears on dispose.
    /// </summary>
    IDisposable CreateScope(string? correlationId = null);
  }

  /// <summary>
  /// Default implementation of ICorrelationIdProvider using AsyncLocal.
  /// GAP-I12: Thread-safe correlation context management.
  /// </summary>
  public class CorrelationIdProvider : ICorrelationIdProvider
  {
    private static readonly AsyncLocal<string?> _correlationId = new();
    private static readonly AsyncLocal<string?> _traceId = new();
    private static readonly AsyncLocal<string?> _spanId = new();

    public string? GetCurrentCorrelationId() => _correlationId.Value;
    public string? GetCurrentTraceId() => _traceId.Value;
    public string? GetCurrentSpanId() => _spanId.Value;

    public void SetCorrelationId(string correlationId)
    {
      _correlationId.Value = correlationId;
    }

    public void SetTraceContext(string? traceId, string? spanId)
    {
      _traceId.Value = traceId;
      _spanId.Value = spanId;
    }

    public void ClearContext()
    {
      _correlationId.Value = null;
      _traceId.Value = null;
      _spanId.Value = null;
    }

    public IDisposable CreateScope(string? correlationId = null)
    {
      var previousCorrelation = _correlationId.Value;
      var previousTrace = _traceId.Value;
      var previousSpan = _spanId.Value;

      if (correlationId != null)
      {
        _correlationId.Value = correlationId;
      }
      else if (_correlationId.Value == null)
      {
        _correlationId.Value = Guid.NewGuid().ToString("N");
      }

      return new CorrelationScope(previousCorrelation, previousTrace, previousSpan);
    }

    private class CorrelationScope : IDisposable
    {
      private readonly string? _previousCorrelation;
      private readonly string? _previousTrace;
      private readonly string? _previousSpan;
      private bool _disposed;

      public CorrelationScope(string? previousCorrelation, string? previousTrace, string? previousSpan)
      {
        _previousCorrelation = previousCorrelation;
        _previousTrace = previousTrace;
        _previousSpan = previousSpan;
      }

      public void Dispose()
      {
        if (!_disposed)
        {
          _correlationId.Value = _previousCorrelation;
          _traceId.Value = _previousTrace;
          _spanId.Value = _previousSpan;
          _disposed = true;
        }
      }
    }
  }
}
