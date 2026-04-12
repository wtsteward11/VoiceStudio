using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VoiceStudio.App.Interop;

/// <summary>
/// Win32 taskbar progress for unpackaged desktop apps via <see cref="ITaskbarList3"/>.
/// Do not use MSIX-only shell APIs here; VoiceStudio is unpackaged.
/// </summary>
[SupportedOSPlatform("windows6.1")]
internal static class TaskbarProgressInterop
{
  internal const int S_OK = 0;

  /// <summary>CLSID for the TaskbarList coclass.</summary>
  internal static readonly Guid ClsidTaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");

  /// <summary>IID for <see cref="ITaskbarList3"/>.</summary>
  internal static readonly Guid IidTaskbarList3 = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");

  private const uint ClsctxInprocServer = 1;

  /// <summary>TBPF_* flags (shellapi).</summary>
  internal enum TbpFlag : uint
  {
    NoProgress = 0,
    Indeterminate = 0x1,
    Normal = 0x2,
    Error = 0x4,
    Paused = 0x8,
  }

  /// <summary>
  /// Creates the shell taskbar list COM object as <see cref="ITaskbarList3"/>.
  /// Caller must invoke <see cref="ITaskbarList3.HrInit"/> once before use.
  /// </summary>
  internal static ITaskbarList3? TryCreateTaskbarList3()
  {
    try
    {
      var clsid = ClsidTaskbarList;
      var iid = IidTaskbarList3;
      var hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out var p);
      if (hr != S_OK || p == IntPtr.Zero)
      {
        Debug.WriteLine($"[TaskbarProgressInterop] CoCreateInstance(TaskbarList) failed: 0x{hr:X8}");
        return null;
      }

      var list = (ITaskbarList3)Marshal.GetObjectForIUnknown(p);
      Marshal.Release(p);
      return list;
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[TaskbarProgressInterop] TryCreateTaskbarList3: {ex.Message}");
      return null;
    }
  }

  [DllImport("ole32.dll", ExactSpelling = true)]
  private static extern int CoCreateInstance(
    ref Guid rclsid,
    IntPtr pUnkOuter,
    uint dwClsContext,
    ref Guid riid,
    out IntPtr ppv);

  /// <summary>
  /// Full vtable through <see cref="ITaskbarList3"/> (inherits <see cref="ITaskbarList"/> / <see cref="ITaskbarList2"/>).
  /// </summary>
  [ComImport]
  [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  internal interface ITaskbarList3
  {
    // ITaskbarList
    [PreserveSig]
    int HrInit();

    [PreserveSig]
    int AddTab(IntPtr hwnd);

    [PreserveSig]
    int DeleteTab(IntPtr hwnd);

    [PreserveSig]
    int ActivateTab(IntPtr hwnd);

    [PreserveSig]
    int SetActiveAlt(IntPtr hwnd);

    // ITaskbarList2
    [PreserveSig]
    int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    // ITaskbarList3
    [PreserveSig]
    int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);

    [PreserveSig]
    int SetProgressState(IntPtr hwnd, TbpFlag tbpFlags);
  }
}
