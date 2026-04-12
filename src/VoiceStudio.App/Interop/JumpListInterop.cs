using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Interop;

/// <summary>
/// Win32 taskbar jump list for unpackaged desktop apps via <see cref="ICustomDestinationList"/>.
/// Do not use <c>Windows.UI.StartScreen.JumpList</c> (MSIX-oriented); VoiceStudio is unpackaged.
/// </summary>
[SupportedOSPlatform("windows6.1")]
internal static class JumpListInterop
{
  private static readonly Guid ClsidDestinationList = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
  private static readonly Guid IidCustomDestinationList = new("6332BFB8-DD1B-4C1B-BBDC-487C23117490");
  private static readonly Guid IidObjectArray = new("92CA9DCD-5622-4B96-A705-6E1E8741B974");
  private static readonly Guid ClsidEnumerableObjectCollection = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
  private static readonly Guid IidObjectCollection = new("2C93C863-07E7-4B3C-B85A-73835A560290");
  private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

  private const uint ClsctxInprocServer = 1;
  private const int S_OK = 0;

  /// <summary>
  /// Rebuilds the taskbar jump list: user tasks (new / open dialog) + optional Recent category.
  /// Best-effort: returns <c>false</c> on any COM failure without throwing.
  /// </summary>
  public static bool TryRebuildJumpList(
    string exePath,
    IReadOnlyList<(string Title, string Arguments, string Description)> userTasks,
    IReadOnlyList<(string Title, string ProjectPath)> recentProjects,
    string recentCategoryName)
  {
    if (string.IsNullOrWhiteSpace(exePath))
    {
      Debug.WriteLine("[JumpListInterop] TryRebuildJumpList: exe path missing.");
      return false;
    }

    ICustomDestinationList? list = null;
    ObjectCollectionHandle? userCollection = null;
    ObjectCollectionHandle? recentCollection = null;
    try
    {
      var clsid = ClsidDestinationList;
      var iid = IidCustomDestinationList;
      var hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out var pList);
      if (hr != S_OK || pList == IntPtr.Zero)
      {
        Debug.WriteLine($"[JumpListInterop] CoCreateInstance(ICustomDestinationList) failed: 0x{hr:X8}");
        return false;
      }

      list = (ICustomDestinationList)Marshal.GetObjectForIUnknown(pList);
      Marshal.Release(pList);

      var riidRemoved = IidObjectArray;
      hr = list.BeginList(out _, ref riidRemoved, out var pRemoved);
      if (hr != S_OK)
      {
        Debug.WriteLine($"[JumpListInterop] BeginList failed: 0x{hr:X8}");
        return false;
      }

      if (pRemoved != IntPtr.Zero)
      {
        Marshal.Release(pRemoved);
      }

      userCollection = CreateObjectCollection();
      if (userCollection == null)
      {
        list.AbortList();
        return false;
      }

      foreach (var task in userTasks)
      {
        var link = CreateShellLink(exePath, task.Arguments, task.Title, task.Description);
        if (link == null)
        {
          continue;
        }

        try
        {
          hr = userCollection.Instance.AddObject(link);
          if (hr != S_OK)
          {
            Debug.WriteLine($"[JumpListInterop] AddObject (user task) failed: 0x{hr:X8}");
          }
        }
        finally
        {
          Marshal.FinalReleaseComObject(link);
        }
      }

      hr = list.AddUserTasks((IObjectArray)userCollection.Instance);
      if (hr != S_OK)
      {
        Debug.WriteLine($"[JumpListInterop] AddUserTasks failed: 0x{hr:X8}");
        list.AbortList();
        return false;
      }

      if (recentProjects.Count > 0)
      {
        recentCollection = CreateObjectCollection();
        if (recentCollection == null)
        {
          list.AbortList();
          return false;
        }

        foreach (var recent in recentProjects)
        {
          if (string.IsNullOrWhiteSpace(recent.ProjectPath))
          {
            continue;
          }

          var args = JumpListArgs.FormatOpenProjectArgument(recent.ProjectPath);
          var link = CreateShellLink(exePath, args, recent.Title, $"Open {recent.Title}");
          if (link == null)
          {
            continue;
          }

          try
          {
            hr = recentCollection.Instance.AddObject(link);
            if (hr != S_OK)
            {
              Debug.WriteLine($"[JumpListInterop] AddObject (recent) failed: 0x{hr:X8}");
            }
          }
          finally
          {
            Marshal.FinalReleaseComObject(link);
          }
        }

        hr = list.AppendCategory(recentCategoryName, (IObjectArray)recentCollection.Instance);
        if (hr != S_OK)
        {
          Debug.WriteLine($"[JumpListInterop] AppendCategory failed: 0x{hr:X8}");
          list.AbortList();
          return false;
        }
      }

      hr = list.CommitList();
      if (hr != S_OK)
      {
        Debug.WriteLine($"[JumpListInterop] CommitList failed: 0x{hr:X8}");
        return false;
      }

      return true;
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[JumpListInterop] TryRebuildJumpList exception: {ex}");
      try
      {
        list?.AbortList();
      }
      catch (Exception abortEx)
      {
        Debug.WriteLine($"[JumpListInterop] AbortList after failure: {abortEx.Message}");
      }

      return false;
    }
    finally
    {
      recentCollection?.Dispose();
      userCollection?.Dispose();
      if (list != null)
      {
        Marshal.FinalReleaseComObject(list);
      }
    }
  }

  /// <summary>
  /// Removes the custom jump list for the current process (best-effort).
  /// </summary>
  public static bool TryDeleteJumpList()
  {
    ICustomDestinationList? list = null;
    try
    {
      var clsid = ClsidDestinationList;
      var iid = IidCustomDestinationList;
      var hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out var pList);
      if (hr != S_OK || pList == IntPtr.Zero)
      {
        return false;
      }

      list = (ICustomDestinationList)Marshal.GetObjectForIUnknown(pList);
      Marshal.Release(pList);
      list.DeleteList(null);
      return true;
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[JumpListInterop] TryDeleteJumpList: {ex.Message}");
      return false;
    }
    finally
    {
      if (list != null)
      {
        Marshal.FinalReleaseComObject(list);
      }
    }
  }

  private static ObjectCollectionHandle? CreateObjectCollection()
  {
    try
    {
      var clsid = ClsidEnumerableObjectCollection;
      var iid = IidObjectCollection;
      var hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out var p);
      if (hr != S_OK || p == IntPtr.Zero)
      {
        Debug.WriteLine($"[JumpListInterop] CoCreateInstance(IObjectCollection) failed: 0x{hr:X8}");
        return null;
      }

      var obj = (IObjectCollection)Marshal.GetObjectForIUnknown(p);
      Marshal.Release(p);
      return new ObjectCollectionHandle(obj);
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[JumpListInterop] CreateObjectCollection: {ex.Message}");
      return null;
    }
  }

  private sealed class ObjectCollectionHandle : IDisposable
  {
    public ObjectCollectionHandle(IObjectCollection instance)
    {
      Instance = instance;
    }

    public IObjectCollection Instance { get; }

    public void Dispose()
    {
      Marshal.FinalReleaseComObject(Instance);
    }
  }

  private static object? CreateShellLink(string exePath, string arguments, string title, string description)
  {
    try
    {
      var shellLinkType = Type.GetTypeFromCLSID(ClsidShellLink);
      if (shellLinkType == null)
      {
        Debug.WriteLine("[JumpListInterop] Shell link type not found.");
        return null;
      }

      var o = Activator.CreateInstance(shellLinkType);
      if (o == null)
      {
        return null;
      }

      var t = shellLinkType;
      t.InvokeMember(
        "SetPath",
        BindingFlags.InvokeMethod,
        null,
        o,
        new object[] { exePath },
        CultureInfo.InvariantCulture);
      if (!string.IsNullOrEmpty(arguments))
      {
        t.InvokeMember(
          "SetArguments",
          BindingFlags.InvokeMethod,
          null,
          o,
          new object[] { arguments },
          CultureInfo.InvariantCulture);
      }

      var desc = string.IsNullOrWhiteSpace(description) ? title : description;
      if (!string.IsNullOrEmpty(desc))
      {
        t.InvokeMember(
          "SetDescription",
          BindingFlags.InvokeMethod,
          null,
          o,
          new object[] { desc },
          CultureInfo.InvariantCulture);
      }

      return o;
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[JumpListInterop] CreateShellLink: {ex.Message}");
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

  [ComImport]
  [Guid("6332BFB8-DD1B-4C1B-BBDC-487C23117490")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface ICustomDestinationList
  {
    [PreserveSig]
    int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string? pszAppID);

    [PreserveSig]
    int BeginList(out uint pcMinSlots, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray? poa);

    [PreserveSig]
    int AppendKnownCategory(uint category);

    [PreserveSig]
    int AddUserTasks(IObjectArray? poa);

    [PreserveSig]
    int CommitList();

    [PreserveSig]
    int GetRemovedDestinations(ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? pszAppID);

    [PreserveSig]
    int AbortList();
  }

  [ComImport]
  [Guid("92CA9DCD-5622-4B96-A705-6E1E8741B974")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IObjectArray
  {
    [PreserveSig]
    int GetCount(out uint pcObjects);

    [PreserveSig]
    int GetAt(uint uiIndex, ref Guid riid, out IntPtr ppv);
  }

  [ComImport]
  [Guid("2C93C863-07E7-4B3C-B85A-73835A560290")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IObjectCollection : IObjectArray
  {
    [PreserveSig]
    int AddObject([MarshalAs(UnmanagedType.IUnknown)] object punk);

    [PreserveSig]
    int AddObjectAt([MarshalAs(UnmanagedType.IUnknown)] object punk, uint uiIndex);

    [PreserveSig]
    int RemoveObjectAt(uint uiIndex);

    [PreserveSig]
    int Clear();
  }
}
