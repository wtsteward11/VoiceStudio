using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Win32 taskbar progress surface (<c>ITaskbarList3</c>). HWND must be set before updates apply.
/// </summary>
public interface ITaskbarProgressService : IDisposable
{
  /// <summary>Associates progress calls with a top-level window handle.</summary>
  void SetWindowHandle(IntPtr hwnd);

  /// <summary>Determinate progress in [0.0, 1.0].</summary>
  void SetNormal(double progress01);

  void SetIndeterminate();

  /// <summary>Shows error state (best-effort); caller may <see cref="Clear"/> immediately after.</summary>
  void SetError();

  /// <summary>Clears taskbar progress (TBPF_NOPROGRESS).</summary>
  void Clear();
}
