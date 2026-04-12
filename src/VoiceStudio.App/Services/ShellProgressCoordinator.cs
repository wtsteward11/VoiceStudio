using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Services;

/// <summary>
/// Single authority for taskbar progress: first registered source wins; additional sources wait in FIFO pending queue.
/// </summary>
public sealed class ShellProgressCoordinator : IShellProgressPublisher
{
  private readonly ITaskbarProgressService _taskbar;
  private readonly object _lock = new();
  private string? _currentForeground;
  private readonly HashSet<string> _active = new(StringComparer.Ordinal);
  private readonly List<string> _pending = new();
  private readonly Dictionary<string, ProgressSnapshot> _last = new(StringComparer.Ordinal);

  public ShellProgressCoordinator(ITaskbarProgressService taskbar)
  {
    _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
  }

  public void ReportProgress(string sourceId, double progress01)
  {
    if (string.IsNullOrWhiteSpace(sourceId))
      return;

    var p = Math.Clamp(progress01, 0.0, 1.0);
    lock (_lock)
    {
      _last[sourceId] = new ProgressSnapshot(p, Indeterminate: false);
      _ = _active.Add(sourceId);

      if (_currentForeground == null)
      {
        _currentForeground = sourceId;
        _taskbar.SetNormal(p);
        return;
      }

      if (string.Equals(_currentForeground, sourceId, StringComparison.Ordinal))
      {
        _taskbar.SetNormal(p);
        return;
      }

      if (!_pending.Contains(sourceId))
        _pending.Add(sourceId);
    }
  }

  public void ReportIndeterminate(string sourceId)
  {
    if (string.IsNullOrWhiteSpace(sourceId))
      return;

    lock (_lock)
    {
      _last[sourceId] = new ProgressSnapshot(0.0, Indeterminate: true);
      _ = _active.Add(sourceId);

      if (_currentForeground == null)
      {
        _currentForeground = sourceId;
        _taskbar.SetIndeterminate();
        return;
      }

      if (string.Equals(_currentForeground, sourceId, StringComparison.Ordinal))
      {
        _taskbar.SetIndeterminate();
        return;
      }

      if (!_pending.Contains(sourceId))
        _pending.Add(sourceId);
    }
  }

  public void ReportError(string sourceId)
  {
    if (string.IsNullOrWhiteSpace(sourceId))
      return;

    lock (_lock)
    {
      if (!string.Equals(_currentForeground, sourceId, StringComparison.Ordinal))
      {
        RemoveSourceTracking(sourceId);
        return;
      }

      _taskbar.SetError();
      _taskbar.Clear();
      _currentForeground = null;
      RemoveSourceTracking(sourceId);
      PromoteNextLocked();
    }
  }

  public void ReportComplete(string sourceId)
  {
    if (string.IsNullOrWhiteSpace(sourceId))
      return;

    TerminalReport(sourceId);
  }

  public void ReportCancelled(string sourceId)
  {
    if (string.IsNullOrWhiteSpace(sourceId))
      return;

    TerminalReport(sourceId);
  }

  private void TerminalReport(string sourceId)
  {
    lock (_lock)
    {
      var wasForeground = string.Equals(_currentForeground, sourceId, StringComparison.Ordinal);
      RemoveSourceTracking(sourceId);

      if (!wasForeground)
        return;

      _taskbar.Clear();
      _currentForeground = null;
      PromoteNextLocked();
    }
  }

  private void RemoveSourceTracking(string sourceId)
  {
    _active.Remove(sourceId);
    _last.Remove(sourceId);
    _pending.RemoveAll(s => string.Equals(s, sourceId, StringComparison.Ordinal));
  }

  private void PromoteNextLocked()
  {
    while (_pending.Count > 0)
    {
      var next = _pending[0];
      _pending.RemoveAt(0);
      if (!_active.Contains(next))
        continue;

      _currentForeground = next;
      ApplySnapshotLocked(next);
      return;
    }
  }

  private void ApplySnapshotLocked(string sourceId)
  {
    if (!_last.TryGetValue(sourceId, out var snap))
    {
      _taskbar.SetIndeterminate();
      return;
    }

    if (snap.Indeterminate)
      _taskbar.SetIndeterminate();
    else
      _taskbar.SetNormal(snap.Progress01);
  }

  private readonly record struct ProgressSnapshot(double Progress01, bool Indeterminate);
}
