using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Interop;

namespace VoiceStudio.App.Helpers;

/// <summary>
/// Helper class for applying Fluent Design 2 materials.
/// 
/// Phase 11.1: Fluent Design 2 Materials
/// Implements Mica, Acrylic, and other backdrop effects.
/// </summary>
/// <remarks>
/// <para><b>Fallback matrix (shell)</b>:</para>
/// <list type="bullet">
/// <item><description>Windows 11 22H2+ — <see cref="MicaController"/> when supported; else Desktop Acrylic when supported.</description></item>
/// <item><description>Windows 10 21H1+ — Desktop Acrylic when supported (Mica typically unavailable).</description></item>
/// <item><description>Older / unsupported — <see cref="MaterialType.None"/>; host keeps merged <c>VSQ.Window.Background</c> gradient; custom title bar still applies.</description></item>
/// </list>
/// </remarks>
public static class MaterialsHelper
{
    /// <summary>
    /// Material type for window backdrops.
    /// </summary>
    public enum MaterialType
    {
        /// <summary>Mica material - recommended for main windows.</summary>
        Mica,
        
        /// <summary>Mica Alt - subtle variant of Mica.</summary>
        MicaAlt,
        
        /// <summary>Desktop Acrylic - for floating windows and dialogs.</summary>
        DesktopAcrylic,
        
        /// <summary>In-app Acrylic - for panels within the app.</summary>
        InAppAcrylic,
        
        /// <summary>No material - solid color background.</summary>
        None
    }

    private static WindowsSystemDispatcherQueueHelper? _wsdqHelper;
    private static MicaController? _micaController;
    private static DesktopAcrylicController? _acrylicController;
    private static SystemBackdropConfiguration? _configurationSource;
    private static Window? _currentWindow;

    /// <summary>
    /// Apply material to a window.
    /// </summary>
    /// <param name="window">The window to apply material to.</param>
    /// <param name="material">The material type to apply.</param>
    /// <returns>True if material was applied successfully.</returns>
    public static bool ApplyMaterial(Window window, MaterialType material)
    {
        // Clean up previous material
        CleanupMaterial();

        _currentWindow = window;

        // Ensure dispatcher queue is available
        if (_wsdqHelper == null)
        {
            _wsdqHelper = new WindowsSystemDispatcherQueueHelper();
            _wsdqHelper.EnsureWindowsSystemDispatcherQueueController();
        }

        // Setup configuration
        _configurationSource = new SystemBackdropConfiguration();
        
        // Hook up theme changes
        if (window.Content is FrameworkElement rootElement)
        {
            rootElement.ActualThemeChanged += (s, e) => UpdateTheme();
            UpdateTheme();
        }

        window.Activated += Window_Activated;
        window.Closed += Window_Closed;

        // Apply the selected material
        return material switch
        {
            MaterialType.Mica => TryApplyMica(window, useMicaAlt: false),
            MaterialType.MicaAlt => TryApplyMica(window, useMicaAlt: true),
            MaterialType.DesktopAcrylic => TryApplyDesktopAcrylic(window),
            MaterialType.InAppAcrylic => true, // Handled via Brush in XAML
            MaterialType.None => true,
            _ => false
        };
    }

    /// <summary>
    /// Get the best available material for the current system.
    /// </summary>
    public static MaterialType GetBestAvailableMaterial()
    {
        if (MicaController.IsSupported())
        {
            return MaterialType.Mica;
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            return MaterialType.DesktopAcrylic;
        }
        else
        {
            return MaterialType.None;
        }
    }

    /// <summary>
    /// Check if Mica is supported on the current system.
    /// </summary>
    public static bool IsMicaSupported() => MicaController.IsSupported();

    /// <summary>
    /// Check if Desktop Acrylic is supported on the current system.
    /// </summary>
    public static bool IsDesktopAcrylicSupported() => DesktopAcrylicController.IsSupported();

    /// <summary>
    /// Re-applies <see cref="SystemBackdropConfiguration"/> theme from the current window content.
    /// Call after merged <see cref="ResourceDictionary"/> theme swaps when <see cref="FrameworkElement.ActualThemeChanged"/>
    /// may not fire.
    /// </summary>
    public static void RefreshSystemBackdropTheme()
    {
        UpdateTheme();
    }

    /// <summary>
    /// Create an in-app acrylic brush for panels.
    /// </summary>
    /// <param name="tintColor">Optional tint color.</param>
    /// <param name="tintOpacity">Tint opacity (0-1).</param>
    /// <param name="fallbackColor">Fallback color if acrylic not supported.</param>
    public static AcrylicBrush CreateInAppAcrylicBrush(
        Windows.UI.Color? tintColor = null,
        double tintOpacity = 0.8,
        Windows.UI.Color? fallbackColor = null)
    {
        return new AcrylicBrush
        {
            TintColor = tintColor ?? Windows.UI.Color.FromArgb(255, 32, 32, 32),
            TintOpacity = tintOpacity,
            FallbackColor = fallbackColor ?? Windows.UI.Color.FromArgb(255, 32, 32, 32)
        };
    }

    /// <summary>
    /// Create a background acrylic brush for overlays.
    /// </summary>
    /// <param name="luminosityOpacity">Luminosity blend opacity.</param>
    public static AcrylicBrush CreateBackgroundAcrylicBrush(double luminosityOpacity = 0.9)
    {
        return new AcrylicBrush
        {
            TintColor = Windows.UI.Color.FromArgb(255, 0, 0, 0),
            TintOpacity = 0.4,
            TintLuminosityOpacity = luminosityOpacity,
            FallbackColor = Windows.UI.Color.FromArgb(255, 40, 40, 40)
        };
    }

    /// <summary>
    /// Cleanup and release material resources.
    /// </summary>
    public static void CleanupMaterial()
    {
        if (_currentWindow != null)
        {
            _currentWindow.Activated -= Window_Activated;
            _currentWindow.Closed -= Window_Closed;
        }

        if (_micaController != null)
        {
            _micaController.Dispose();
            _micaController = null;
        }

        if (_acrylicController != null)
        {
            _acrylicController.Dispose();
            _acrylicController = null;
        }

        _configurationSource = null;
        _currentWindow = null;
    }

    #region Private Methods

    private static bool TryApplyMica(Window window, bool useMicaAlt)
    {
        if (!MicaController.IsSupported())
        {
            return false;
        }

        _micaController = new MicaController
        {
            Kind = useMicaAlt ? MicaKind.BaseAlt : MicaKind.Base
        };

        _micaController.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>());
        _micaController.SetSystemBackdropConfiguration(_configurationSource);

        return true;
    }

    private static bool TryApplyDesktopAcrylic(Window window)
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return false;
        }

        _acrylicController = new DesktopAcrylicController
        {
            TintColor = Windows.UI.Color.FromArgb(255, 32, 32, 32),
            TintOpacity = 0.8f,
            LuminosityOpacity = 0.95f
        };

        _acrylicController.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

        return true;
    }

    private static void UpdateTheme()
    {
        if (_configurationSource == null || _currentWindow == null)
        {
            return;
        }

        if (_currentWindow.Content is FrameworkElement rootElement)
        {
            _configurationSource.Theme = rootElement.ActualTheme switch
            {
                ElementTheme.Dark => SystemBackdropTheme.Dark,
                ElementTheme.Light => SystemBackdropTheme.Light,
                _ => SystemBackdropTheme.Default
            };
        }
    }

    private static void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_configurationSource != null)
        {
            _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }
    }

    private static void Window_Closed(object sender, WindowEventArgs args)
    {
        CleanupMaterial();
    }

    #endregion
}

/// <summary>
/// Helper class for Windows System Dispatcher Queue.
/// Required for backdrop controllers.
/// </summary>
internal class WindowsSystemDispatcherQueueHelper
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        internal int dwSize;
        internal int threadType;
        internal int apartmentType;
    }

    [System.Runtime.InteropServices.DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        [System.Runtime.InteropServices.In] DispatcherQueueOptions options,
        [System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.IUnknown)]
        ref object? dispatcherQueueController);

    private object? _dispatcherQueueController;

    public void EnsureWindowsSystemDispatcherQueueController()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null)
        {
            // Already initialized
            return;
        }

        if (_dispatcherQueueController == null)
        {
            DispatcherQueueOptions options;
            options.dwSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DispatcherQueueOptions));
            options.threadType = 2;    // DQTYPE_THREAD_CURRENT
            options.apartmentType = 2; // DQTAT_COM_STA

            _ = CreateDispatcherQueueController(options, ref _dispatcherQueueController);
        }
    }
}
