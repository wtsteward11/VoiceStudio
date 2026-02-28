using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Native Win32 file dialog using IFileOpenDialog COM interface.
  /// Used as fallback when WinRT FileOpenPicker fails with COMException 0x80004005.
  /// </summary>
  public static class NativeFileDialog
  {
    // COM GUIDs
    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    private static readonly Guid IID_IFileOpenDialog = new("d57c7288-d4ad-4768-be02-9d969532d960");
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    // File dialog options
    [Flags]
    private enum FOS : uint
    {
      FOS_OVERWRITEPROMPT = 0x00000002,
      FOS_STRICTFILETYPES = 0x00000004,
      FOS_NOCHANGEDIR = 0x00000008,
      FOS_PICKFOLDERS = 0x00000020,
      FOS_FORCEFILESYSTEM = 0x00000040,
      FOS_ALLNONSTORAGEITEMS = 0x00000080,
      FOS_NOVALIDATE = 0x00000100,
      FOS_ALLOWMULTISELECT = 0x00000200,
      FOS_PATHMUSTEXIST = 0x00000800,
      FOS_FILEMUSTEXIST = 0x00001000,
      FOS_CREATEPROMPT = 0x00002000,
      FOS_SHAREAWARE = 0x00004000,
      FOS_NOREADONLYRETURN = 0x00008000,
      FOS_NOTESTFILECREATE = 0x00010000,
      FOS_HIDEMRUPLACES = 0x00020000,
      FOS_HIDEPINNEDPLACES = 0x00040000,
      FOS_NODEREFERENCELINKS = 0x00100000,
      FOS_DONTADDTORECENT = 0x02000000,
      FOS_FORCESHOWHIDDEN = 0x10000000,
      FOS_DEFAULTNOMINIMODE = 0x20000000,
      FOS_FORCEPREVIEWPANEON = 0x40000000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
      [MarshalAs(UnmanagedType.LPWStr)]
      public string pszName;
      [MarshalAs(UnmanagedType.LPWStr)]
      public string pszSpec;
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
      void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
      void GetParent(out IShellItem ppsi);
      void GetDisplayName(uint sigdnName, out IntPtr ppszName);
      void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
      void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
      void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
      void GetPropertyStore(uint flags, ref Guid riid, out IntPtr ppv);
      void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
      void GetAttributes(uint attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
      void GetCount(out uint pdwNumItems);
      void GetItemAt(uint dwIndex, out IShellItem ppsi);
      void EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
      [PreserveSig]
      int Show(IntPtr parent);
      void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
      void SetFileTypeIndex(uint iFileType);
      void GetFileTypeIndex(out uint piFileType);
      void Advise(IntPtr pfde, out uint pdwCookie);
      void Unadvise(uint dwCookie);
      void SetOptions(FOS fos);
      void GetOptions(out FOS pfos);
      void SetDefaultFolder(IShellItem psi);
      void SetFolder(IShellItem psi);
      void GetFolder(out IShellItem ppsi);
      void GetCurrentSelection(out IShellItem ppsi);
      void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
      void GetFileName(out IntPtr pszName);
      void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
      void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
      void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
      void GetResult(out IShellItem ppsi);
      void AddPlace(IShellItem psi, uint fdap);
      void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
      void Close(int hr);
      void SetClientGuid(ref Guid guid);
      void ClearClientData();
      void SetFilter(IntPtr pFilter);
      void GetResults(out IShellItemArray ppenum);
      void GetSelectedItems(out IShellItemArray ppsai);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
      ref Guid clsid,
      IntPtr pUnkOuter,
      uint dwClsContext,
      ref Guid riid,
      out IntPtr ppv);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    /// <summary>
    /// Shows an open file dialog and returns the selected file path.
    /// </summary>
    public static Task<string?> ShowOpenFileDialogAsync(IntPtr hwndOwner, string title, params string[] fileTypes)
    {
      return Task.Run(() =>
      {
        try
        {
          var clsid = CLSID_FileOpenDialog;
          var iid = IID_IFileOpenDialog;

          int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out IntPtr pDialog);
          if (hr != 0)
          {
            System.Diagnostics.Debug.WriteLine($"[NativeFileDialog] CoCreateInstance failed: 0x{hr:X8}");
            return null;
          }

          var dialog = (IFileOpenDialog)Marshal.GetObjectForIUnknown(pDialog);
          Marshal.Release(pDialog);

          // Set title
          dialog.SetTitle(title);

          // Set file type filters
          if (fileTypes.Length > 0)
          {
            var specs = new COMDLG_FILTERSPEC[2];
            specs[0] = new COMDLG_FILTERSPEC
            {
              pszName = "Audio Files",
              pszSpec = string.Join(";", Array.ConvertAll(fileTypes, t => "*" + (t.StartsWith(".") ? t : "." + t)))
            };
            specs[1] = new COMDLG_FILTERSPEC
            {
              pszName = "All Files",
              pszSpec = "*.*"
            };
            dialog.SetFileTypes(2, specs);
          }

          // Set options
          dialog.GetOptions(out FOS options);
          options |= FOS.FOS_FILEMUSTEXIST | FOS.FOS_PATHMUSTEXIST | FOS.FOS_FORCEFILESYSTEM;
          dialog.SetOptions(options);

          // Show dialog
          hr = dialog.Show(hwndOwner);
          if (hr != 0)
          {
            // User cancelled (hr = 0x800704C7 means cancelled)
            System.Diagnostics.Debug.WriteLine($"[NativeFileDialog] Dialog cancelled or failed: 0x{hr:X8}");
            return null;
          }

          // Get result
          dialog.GetResult(out IShellItem item);
          item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr pszPath);
          string? path = Marshal.PtrToStringUni(pszPath);
          CoTaskMemFree(pszPath);

          return path;
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[NativeFileDialog] Exception: {ex.Message}");
          return null;
        }
      });
    }

    /// <summary>
    /// Shows an open file dialog for multiple file selection.
    /// </summary>
    public static Task<string[]?> ShowOpenFilesDialogAsync(IntPtr hwndOwner, string title, params string[] fileTypes)
    {
      return Task.Run(() =>
      {
        try
        {
          var clsid = CLSID_FileOpenDialog;
          var iid = IID_IFileOpenDialog;

          int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out IntPtr pDialog);
          if (hr != 0)
          {
            System.Diagnostics.Debug.WriteLine($"[NativeFileDialog] CoCreateInstance failed: 0x{hr:X8}");
            return null;
          }

          var dialog = (IFileOpenDialog)Marshal.GetObjectForIUnknown(pDialog);
          Marshal.Release(pDialog);

          // Set title
          dialog.SetTitle(title);

          // Set file type filters
          if (fileTypes.Length > 0)
          {
            var specs = new COMDLG_FILTERSPEC[2];
            specs[0] = new COMDLG_FILTERSPEC
            {
              pszName = "Audio Files",
              pszSpec = string.Join(";", Array.ConvertAll(fileTypes, t => "*" + (t.StartsWith(".") ? t : "." + t)))
            };
            specs[1] = new COMDLG_FILTERSPEC
            {
              pszName = "All Files",
              pszSpec = "*.*"
            };
            dialog.SetFileTypes(2, specs);
          }

          // Set options (allow multiple selection)
          dialog.GetOptions(out FOS options);
          options |= FOS.FOS_FILEMUSTEXIST | FOS.FOS_PATHMUSTEXIST | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_ALLOWMULTISELECT;
          dialog.SetOptions(options);

          // Show dialog
          hr = dialog.Show(hwndOwner);
          if (hr != 0)
          {
            System.Diagnostics.Debug.WriteLine($"[NativeFileDialog] Dialog cancelled or failed: 0x{hr:X8}");
            return null;
          }

          // Get results
          dialog.GetResults(out IShellItemArray items);
          items.GetCount(out uint count);

          var paths = new List<string>();
          for (uint i = 0; i < count; i++)
          {
            items.GetItemAt(i, out IShellItem item);
            item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr pszPath);
            string? path = Marshal.PtrToStringUni(pszPath);
            CoTaskMemFree(pszPath);
            if (!string.IsNullOrEmpty(path))
            {
              paths.Add(path);
            }
          }

          return paths.Count > 0 ? paths.ToArray() : null;
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[NativeFileDialog] Exception: {ex.Message}");
          return null;
        }
      });
    }
  }
}
