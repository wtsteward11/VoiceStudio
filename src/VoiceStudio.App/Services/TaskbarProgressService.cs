using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VoiceStudio.App.Interop;

namespace VoiceStudio.App.Services;

/// <summary>
/// Best-effort <see cref="ITaskbarList3"/> wrapper for unpackaged WinUI windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TaskbarProgressService : ITaskbarProgressService
{
  private readonly object _sync = new();
  private TaskbarProgressInterop.ITaskbarList3? _list;
  private IntPtr _hwnd;
  private bool _disposed;

  public void SetWindowHandle(IntPtr hwnd)
  {
    lock (_sync)
    {
      _hwnd = hwnd;
    }
  }

  public void SetNormal(double progress01)
  {
    lock (_sync)
    {
      if (_hwnd == IntPtr.Zero || !EnsureListLocked())
        return;

      try
      {
        var p = Math.Clamp(progress01, 0.0, 1.0);
        var completed = (ulong)Math.Round(p * 10000.0);
        var hr = _list!.SetProgressState(_hwnd, TaskbarProgressInterop.TbpFlag.Normal);
        if (hr != TaskbarProgressInterop.S_OK)
        {
          Debug.WriteLine($"[TaskbarProgressService] SetProgressState(Normal) failed: 0x{hr:X8}");
        }

        hr = _list.SetProgressValue(_hwnd, completed, 10000);
        if (hr != TaskbarProgressInterop.S_OK)
        {
          Debug.WriteLine($"[TaskbarProgressService] SetProgressValue failed: 0x{hr:X8}");
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[TaskbarProgressService] SetNormal: {ex.Message}");
      }
    }
  }

  public void SetIndeterminate()
  {
    lock (_sync)
    {
      if (_hwnd == IntPtr.Zero || !EnsureListLocked())
        return;

      try
      {
        var hr = _list!.SetProgressState(_hwnd, TaskbarProgressInterop.TbpFlag.Indeterminate);
        if (hr != TaskbarProgressInterop.S_OK)
        {
          Debug.WriteLine($"[TaskbarProgressService] SetProgressState(Indeterminate) failed: 0x{hr:X8}");
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[TaskbarProgressService] SetIndeterminate: {ex.Message}");
      }
    }
  }

  public void SetError()
  {
    lock (_sync)
    {
      if (_hwnd == IntPtr.Zero || !EnsureListLocked())
        return;

      try
      {
        var hr = _list!.SetProgressState(_hwnd, TaskbarProgressInterop.TbpFlag.Error);
        if (hr != TaskbarProgressInterop.S_OK)
        {
          Debug.WriteLine($"[TaskbarProgressService] SetProgressState(Error) failed: 0x{hr:X8}");
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[TaskbarProgressService] SetError: {ex.Message}");
      }
    }
  }

  public void Clear()
  {
    lock (_sync)
    {
      if (_hwnd == IntPtr.Zero || !EnsureListLocked())
        return;

      try
      {
        var hr = _list!.SetProgressState(_hwnd, TaskbarProgressInterop.TbpFlag.NoProgress);
        if (hr != TaskbarProgressInterop.S_OK)
        {
          Debug.WriteLine($"[TaskbarProgressService] SetProgressState(NoProgress) failed: 0x{hr:X8}");
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[TaskbarProgressService] Clear: {ex.Message}");
      }
    }
  }

  public void Dispose()
  {
    lock (_sync)
    {
      if (_disposed)
        return;
      _disposed = true;
      if (_list != null)
      {
        try
        {
          Marshal.FinalReleaseComObject(_list);
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[TaskbarProgressService] Dispose release: {ex.Message}");
        }

        _list = null;
      }
    }
  }

  /// <summary>Must hold <see cref="_sync"/>.</summary>
  private bool EnsureListLocked()
  {
    if (_disposed)
      return false;
    if (_list != null)
      return true;

    var created = TaskbarProgressInterop.TryCreateTaskbarList3();
    if (created == null)
      return false;

    try
    {
      var hr = created.HrInit();
      if (hr != TaskbarProgressInterop.S_OK)
      {
        Debug.WriteLine($"[TaskbarProgressService] HrInit failed: 0x{hr:X8}");
        Marshal.FinalReleaseComObject(created);
        return false;
      }

      _list = created;
      return true;
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[TaskbarProgressService] EnsureList: {ex.Message}");
      try
      {
        Marshal.FinalReleaseComObject(created);
      }
      catch (Exception relEx)
      {
        Debug.WriteLine($"[TaskbarProgressService] EnsureList release: {relEx.Message}");
      }

      return false;
    }
  }
}
