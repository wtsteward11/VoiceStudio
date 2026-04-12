using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using VoiceStudio.App.Interop;

namespace VoiceStudio.App.Services;

/// <summary>
/// Canonical taskbar jump list authority for unpackaged Win32: projects <see cref="RecentProjectsService"/>,
/// static File-menu tasks, and Win32 <c>ICustomDestinationList</c> (not MSIX <c>Windows.UI.StartScreen.JumpList</c>).
/// </summary>
public sealed class JumpListService : IDisposable
{
  public const int DebounceMilliseconds = 500;
  public const int MaxRecentJumpListItems = 10;
  private const string RecentCategoryName = "Recent";

  private readonly RecentProjectsService _recents;
  private readonly DispatcherQueue _dispatcherQueue;
  private readonly object _timerLock = new();
  private Timer? _debounceTimer;
  private bool _disposed;

  public JumpListService(RecentProjectsService recents, DispatcherQueue dispatcherQueue)
  {
    _recents = recents ?? throw new ArgumentNullException(nameof(recents));
    _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    _recents.PropertyChanged += OnRecentsPropertyChanged;
  }

  private void OnRecentsPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (_disposed)
    {
      return;
    }

    _ = e;
    ScheduleDebouncedRebuild();
  }

  private void ScheduleDebouncedRebuild()
  {
    lock (_timerLock)
    {
      if (_debounceTimer == null)
      {
        _debounceTimer = new Timer(
          _ =>
          {
            try
            {
              _dispatcherQueue.TryEnqueue(RebuildCore);
            }
            catch (Exception ex)
            {
              Debug.WriteLine($"[JumpList] Debounce enqueue failed: {ex.Message}");
            }
          },
          null,
          DebounceMilliseconds,
          Timeout.Infinite);
      }
      else
      {
        _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
      }
    }
  }

  /// <summary>
  /// Enqueues a full jump list rebuild (initial load, manual refresh).
  /// </summary>
  public void UpdateJumpList()
  {
    if (_disposed)
    {
      return;
    }

    _dispatcherQueue.TryEnqueue(RebuildCore);
  }

  private void RebuildCore()
  {
    if (_disposed)
    {
      return;
    }

    try
    {
      var exe = Environment.ProcessPath;
      if (string.IsNullOrEmpty(exe))
      {
        Debug.WriteLine("[JumpList] Environment.ProcessPath is empty.");
        return;
      }

      var staticTasks = new List<(string Title, string Arguments, string Description)>
      {
        ("New Project", JumpListArgs.NewProject, "Create a new VoiceStudio project"),
        ("Open Project", JumpListArgs.OpenDialog, "Open an existing project"),
      };

      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var recents = new List<(string Title, string ProjectPath)>();
      foreach (var p in _recents.AllProjects)
      {
        if (recents.Count >= MaxRecentJumpListItems)
        {
          break;
        }

        if (string.IsNullOrWhiteSpace(p.Path) || !seen.Add(p.Path))
        {
          continue;
        }

        var title = string.IsNullOrWhiteSpace(p.Name)
          ? System.IO.Path.GetFileNameWithoutExtension(p.Path)
          : p.Name;
        recents.Add((title, p.Path));
      }

      JumpListInterop.TryRebuildJumpList(exe, staticTasks, recents, RecentCategoryName);
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[JumpList] RebuildCore: {ex}");
    }
  }

  /// <summary>
  /// Best-effort removal of custom jump list entries for this app.
  /// </summary>
  public void ClearJumpList()
  {
    _dispatcherQueue.TryEnqueue(() =>
    {
      try
      {
        JumpListInterop.TryDeleteJumpList();
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[JumpList] ClearJumpList: {ex.Message}");
      }
    });
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _recents.PropertyChanged -= OnRecentsPropertyChanged;
    lock (_timerLock)
    {
      _debounceTimer?.Dispose();
      _debounceTimer = null;
    }
  }
}
