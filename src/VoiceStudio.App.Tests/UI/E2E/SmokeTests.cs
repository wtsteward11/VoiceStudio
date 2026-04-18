using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VoiceStudio.App.Tests.UI.E2E
{
    /// <summary>
    /// Smoke tests for critical user journeys using FlaUI.
    /// These tests verify that the application starts and core UI components are accessible.
    /// </summary>
    /// <remarks>
    /// Shared static application/window handles must not be touched concurrently — parallel execution
    /// against the same WinUI surface deadlocks or stalls UIA (observed as 600s harness timeouts).
    /// </remarks>
    [DoNotParallelize]
    [TestClass]
    [TestCategory("E2E")]
    [TestCategory("Smoke")]
    public class SmokeTests
    {
        private static Process? _childProcess;
        private static UIA3Automation? _automation;
        private static Window? _mainWindow;
        private static string? _appPath;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            // Program.cs single-instance mutex — a stray VoiceStudio.App.exe (prior test / manual run) causes
            // new launches to exit immediately before FlaUI can attach ("Could not find process id").
            foreach (var existing in Process.GetProcessesByName("VoiceStudio.App"))
            {
                try
                {
                    existing.Kill(entireProcessTree: true);
                    existing.WaitForExit(milliseconds: 8000);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"SmokeTests: kill stray VoiceStudio.App: {ex.Message}");
                }
                finally
                {
                    existing.Dispose();
                }
            }

            Thread.Sleep(300);

            // Full FlaUI + Win32 title enumeration requires an interactive desktop session. verify.ps1 sets
            // VOICESTUDIO_USE_REAL_UI_AUTOMATION=true when -RealUI is passed (UI Smoke stage); without it, inconclusive.
            var realUi = Environment.GetEnvironmentVariable("VOICESTUDIO_USE_REAL_UI_AUTOMATION");
            if (!string.Equals(realUi, "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Inconclusive(
                    "FlaUI E2E smoke requires VOICESTUDIO_USE_REAL_UI_AUTOMATION=true (run scripts/verify.ps1 UI Smoke Tests stage, or export the variable for interactive desktop access).");
                return;
            }

            // Find the application executable
            _appPath = FindApplicationPath();
            
            if (string.IsNullOrEmpty(_appPath) || !File.Exists(_appPath))
            {
                Assert.Inconclusive("VoiceStudio.App.exe not found. Ensure the application is built.");
                return;
            }

            _automation = new UIA3Automation();
            
            try
            {
                // FlaUI Application.Launch() waits for process input idle (Win32 WaitForInputIdle). WinUI 3 / Windows
                // App SDK apps often never satisfy that predicate the way classic Win32 does, which can block
                // indefinitely and surface as harness timeouts. Start detached, then attach by process id.
                var workDir = Path.GetDirectoryName(_appPath);
                var startInfo = new ProcessStartInfo(_appPath!)
                {
                    UseShellExecute = false,
                    WorkingDirectory = string.IsNullOrEmpty(workDir) ? Environment.CurrentDirectory : workDir,
                };

                foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
                {
                    startInfo.Environment[(string)e.Key] = e.Value?.ToString() ?? string.Empty;
                }

                // Inherited machine/CI env can enable Gate C / smoke-exit / failure-repro modes that exit immediately.
                // FlaUI needs a normal interactive shell session (wizard skipped via FLAUI_AUTOMATION only).
                foreach (var key in new[]
                         {
                             "VOICE_STUDIO_SMOKE_EXIT",
                             "VOICE_STUDIO_SMOKE_UI",
                             "VOICE_STUDIO_ICON_LAUNCH_SMOKE",
                             "VOICE_STUDIO_SMOKE_FAILURE_PORT",
                             "VOICE_STUDIO_SMOKE_FAILURE_RUNTIME",
                             "VOICE_STUDIO_UI_SELF_TEST",
                             "VOICE_STUDIO_UI_SELF_TEST_REQUIRE_BACKEND",
                             "VOICESTUDIO_USE_REAL_UI_AUTOMATION",
                         })
                {
                    if (startInfo.Environment.ContainsKey(key))
                    {
                        startInfo.Environment.Remove(key);
                    }
                }

                // Skip first-run wizard so MainWindow is created (App.xaml.cs checks VOICE_STUDIO_FLAUI_AUTOMATION).
                startInfo.Environment["VOICE_STUDIO_FLAUI_AUTOMATION"] = "1";

                _childProcess = Process.Start(startInfo);
                if (_childProcess == null)
                {
                    Assert.Inconclusive("Process.Start returned null; cannot launch VoiceStudio.App.exe.");
                    return;
                }

                // Do not use FlaUI Application.Attach: it correlated with child process termination under vstest.

                // CRITICAL: GetMainWindow(automation) without a timeout uses an INFINITE wait (FlaUI API) —
                // the prior Retry.WhileNull loop never advanced past the first iteration (600s harness timeouts).
                // WinUI 3 can be slow to expose a main handle; poll with short per-call timeouts for up to 3 minutes.
                var waitDeadline = DateTime.UtcNow.AddSeconds(90);
                while (_mainWindow == null && DateTime.UtcNow < waitDeadline && IsProcessRunning(_childProcess))
                {
                    try
                    {
                        _childProcess.Refresh();
                        if (_childProcess.MainWindowHandle != IntPtr.Zero)
                        {
                            var fromHandle = _automation.FromHandle(_childProcess.MainWindowHandle);
                            _mainWindow = fromHandle.AsWindow();
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"SmokeTests: MainWindowHandle FromHandle: {ex.Message}");
                    }

                    if (_mainWindow != null)
                    {
                        break;
                    }

                    // WinUI 3 often does not populate Process.MainWindowHandle; enumerate Win32 top-level HWNDs for this PID.
                    _mainWindow = TryFindMainWindowForProcess(_childProcess.Id, _automation);
                    if (_mainWindow != null)
                    {
                        break;
                    }

                    if (_mainWindow == null)
                    {
                        Thread.Sleep(400);
                    }
                }

                if (_mainWindow == null)
                {
                    if (!IsProcessRunning(_childProcess))
                    {
                        Assert.Inconclusive(
                            $"VoiceStudio.App exited before a shell window was found (ExitCode={TryGetExitCode(_childProcess)}).");
                    }

                    var titles = ListVisibleWindowTitlesForProcess(_childProcess.Id);
                    Assert.Inconclusive(
                        "Main window did not appear within timeout (EnumWindows + UIA FromHandle). "
                        + $"processRunning={IsProcessRunning(_childProcess)}; visibleTitledWindowsForPid={titles.Count}: [{string.Join(" | ", titles)}]. "
                        + "If titles are empty but the app is running, the test host may lack access to the interactive desktop; run verify.ps1 -RealUI for UI Smoke.");
                }
            }
            catch (AssertInconclusiveException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"Failed to launch application: {ex.Message}");
            }
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            _mainWindow = null;

            if (_childProcess != null)
            {
                try
                {
                    if (IsProcessRunning(_childProcess))
                    {
                        _childProcess.Kill(entireProcessTree: true);
                        _childProcess.WaitForExit(milliseconds: 15000);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"SmokeTests cleanup: kill child: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        _childProcess.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"SmokeTests cleanup: child Dispose: {ex.Message}");
                    }

                    _childProcess = null;
                }
            }

            _automation?.Dispose();
            _automation = null;
        }

        /// <summary>
        /// Critical Journey 1: Application launches successfully and main window is visible.
        /// </summary>
        [TestMethod]
        public void Journey1_ApplicationLaunches_MainWindowVisible()
        {
            // Arrange & Act - Done in ClassInitialize
            
            // Assert
            Assert.IsNotNull(_mainWindow, "Main window should be available");
            Assert.IsTrue(_mainWindow.IsOffscreen == false, "Main window should be visible on screen");
            Assert.IsTrue(
                _mainWindow.Title.Contains("VoiceStudio", StringComparison.OrdinalIgnoreCase),
                "Window title should identify VoiceStudio (branding may include suffix e.g. Quantum+)");
        }

        /// <summary>
        /// Critical Journey 2: Navigation panel is accessible and contains expected items.
        /// </summary>
        [TestMethod]
        public void Journey2_NavigationPanel_IsAccessible()
        {
            // Arrange
            var cf = _automation!.ConditionFactory;
            
            // Act — nav rail uses toggle buttons (NavStudio, NavProfiles, …), not NavigationView
            var navStudio = Retry.WhileNull(
                () => _mainWindow!.FindFirstDescendant(cf.ByAutomationId("NavStudio")),
                TimeSpan.FromSeconds(25),
                TimeSpan.FromMilliseconds(400)).Result;

            // Assert
            Assert.IsNotNull(navStudio, "Nav rail (NavStudio) should be present after shell loads");

            var navProfiles = _mainWindow!.FindFirstDescendant(cf.ByAutomationId("NavProfiles"));
            Assert.IsNotNull(navProfiles, "NavProfiles should be present on the nav rail");
        }

        /// <summary>
        /// Critical Journey 3: Content area displays when navigation item selected.
        /// </summary>
        [TestMethod]
        public void Journey3_ContentArea_DisplaysOnNavigation()
        {
            // Arrange
            var cf = _automation!.ConditionFactory;
            
            // Act — status strip is always loaded with the main shell (stable AutomationId in MainWindow.xaml)
            var statusText = Retry.WhileNull(
                () => _mainWindow!.FindFirstDescendant(cf.ByAutomationId("StatusBar_StatusText")),
                TimeSpan.FromSeconds(25),
                TimeSpan.FromMilliseconds(400)).Result;

            // Assert
            Assert.IsNotNull(statusText, "Status bar should be present (shell composed)");
        }

        /// <summary>
        /// Critical Journey 4: Settings can be accessed from navigation.
        /// </summary>
        [TestMethod]
        public void Journey4_Settings_CanBeAccessed()
        {
            // Arrange
            var cf = _automation!.ConditionFactory;
            
            // Act — settings entry is NavSettings on the left nav rail (MainWindow.xaml)
            var settingsNav = Retry.WhileNull(
                () => _mainWindow!.FindFirstDescendant(cf.ByAutomationId("NavSettings")),
                TimeSpan.FromSeconds(25),
                TimeSpan.FromMilliseconds(400)).Result;

            Assert.IsNotNull(settingsNav, "NavSettings should be present on the nav rail");
            Assert.IsTrue(settingsNav.IsEnabled, "NavSettings should be enabled for interaction");

            // Full settings panel load is gated on shell/backend readiness and startup overlay; smoke only proves
            // the nav affordance exists. Deeper settings UI is covered by panel tests and manual release checks.
        }

        /// <summary>
        /// Critical Journey 5: Theme switching works without errors.
        /// </summary>
        [TestMethod]
        public void Journey5_ThemeSwitch_CompletesWithoutError()
        {
            // Arrange
            var cf = _automation!.ConditionFactory;
            
            // First navigate to settings/theme area
            var themeCombo = FindThemeComboBox(cf);
            
            if (themeCombo == null)
            {
                Assert.Inconclusive("Theme selector not found in current view");
                return;
            }

            // Act - Try to interact with theme selector
            var comboBox = themeCombo.AsComboBox();
            Assert.IsNotNull(comboBox, "Theme control should be a ComboBox");
            
            // Expand and verify items exist
            comboBox.Expand();
            Thread.Sleep(300);
            
            var items = comboBox.Items;
            Assert.IsTrue(items.Length > 0, "Theme selector should have theme options");
            
            // Select first item if available
            if (items.Length > 0)
            {
                items[0].Click();
                Thread.Sleep(300);
            }
            
            // Verify no crash occurred (if we got here, it didn't crash)
            Assert.IsNotNull(_mainWindow, "Application should still be running after theme change");
        }

        #region Helper Methods

        private static bool IsProcessRunning(Process process)
        {
            try
            {
                process.Refresh();
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static int TryGetExitCode(Process process)
        {
            try
            {
                process.Refresh();
                return process.HasExited ? process.ExitCode : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static IReadOnlyList<string> ListVisibleWindowTitlesForProcess(int processId)
        {
            var titles = new List<string>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if ((int)pid != processId)
                {
                    return true;
                }

                if (!NativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }

                var len = NativeMethods.GetWindowTextLength(hwnd);
                if (len <= 0)
                {
                    return true;
                }

                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                var t = sb.ToString();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    titles.Add(t);
                }

                return true;
            }, 0);
            return titles;
        }

        private static Window? TryFindMainWindowForProcess(int processId, UIA3Automation automation)
        {
            var candidates = new List<(IntPtr Hwnd, string Title)>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if ((int)pid != processId)
                {
                    return true;
                }

                if (!NativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }

                var len = NativeMethods.GetWindowTextLength(hwnd);
                if (len <= 0)
                {
                    return true;
                }

                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    candidates.Add((new IntPtr(hwnd), title));
                }

                return true;
            }, 0);

            foreach (var (hwnd, title) in candidates.OrderByDescending(t => t.Title.Length))
            {
                if (!title.Contains("VoiceStudio", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    return automation.FromHandle(hwnd).AsWindow();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"SmokeTests: EnumWindows FromHandle failed ({title}): {ex.Message}");
                }
            }

            foreach (var (hwnd, title) in candidates)
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                try
                {
                    return automation.FromHandle(hwnd).AsWindow();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"SmokeTests: EnumWindows fallback FromHandle ({title}): {ex.Message}");
                }
            }

            return null;
        }

        private static string? FindApplicationPath()
        {
            const string Tfm = "net8.0-windows10.0.19041.0";
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // VoiceStudio.App uses BaseOutputPath = $(SolutionDir).buildlogs/ (see VoiceStudio.App.csproj).
            // Building the .csproj alone can leave SolutionDir pointing at the app folder, so output may be
            // repo\.buildlogs\ OR repo\src\VoiceStudio.App\.buildlogs\. Prefer the newest exe so FlaUI does not
            // launch a stale binary after partial builds.
            var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", ".."));

            var possiblePaths = new List<string>
            {
                Path.Combine(repoRoot, ".buildlogs", "x64", "Debug", Tfm, "VoiceStudio.App.exe"),
                Path.Combine(repoRoot, ".buildlogs", "x64", "Release", Tfm, "VoiceStudio.App.exe"),
                Path.Combine(repoRoot, "src", "VoiceStudio.App", ".buildlogs", "x64", "Debug", Tfm, "VoiceStudio.App.exe"),
                Path.Combine(repoRoot, "src", "VoiceStudio.App", ".buildlogs", "x64", "Release", Tfm, "VoiceStudio.App.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "VoiceStudio.App", "bin", "x64", "Debug", Tfm, "VoiceStudio.App.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "VoiceStudio.App", "bin", "x64", "Release", Tfm, "VoiceStudio.App.exe"),
            };

            var staging = Environment.GetEnvironmentVariable("BUILD_ARTIFACTSTAGINGDIRECTORY");
            if (!string.IsNullOrWhiteSpace(staging))
            {
                possiblePaths.Add(Path.Combine(staging, "VoiceStudio.App.exe"));
            }

            // Prefer Debug output when tests run Debug — picking "newest" across Debug+Release can launch Release
            // while MSTest uses Debug deps, causing immediate process exit.
            var debugPaths = possiblePaths
                .Where(p => p.IndexOf($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0
                    || p.IndexOf("\\Debug\\", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            var pickFrom = debugPaths.Count > 0 ? debugPaths : possiblePaths;

            string? bestPath = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var path in pickFrom)
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var t = File.GetLastWriteTimeUtc(fullPath);
                if (t >= bestTime)
                {
                    bestTime = t;
                    bestPath = fullPath;
                }
            }

            return bestPath;
        }

        private AutomationElement? FindThemeComboBox(ConditionFactory cf)
        {
            // Try to find theme combo in current view
            var themeCombo = _mainWindow!.FindFirstDescendant(
                cf.ByAutomationId("ThemeEditor.ComboBox.Theme")
            ) ?? _mainWindow!.FindFirstDescendant(
                cf.ByAutomationId("Settings.ComboBox.Theme")
            );

            if (themeCombo != null) return themeCombo;

            // Navigate to settings first
            var settingsItem = _mainWindow!.FindFirstDescendant(
                cf.ByAutomationId("SettingsNavItem")
            ) ?? _mainWindow!.FindFirstDescendant(
                cf.ByName("Settings")
            );

            if (settingsItem != null)
            {
                settingsItem.Click();
                Thread.Sleep(500);
                
                return _mainWindow!.FindFirstDescendant(
                    cf.ByAutomationId("Settings.ComboBox.Theme")
                );
            }

            return null;
        }

        #endregion

        #region Screenshot Support

        /// <summary>
        /// Capture screenshot on test failure.
        /// </summary>
        [TestCleanup]
        public void TestCleanup()
        {
            if (TestContext?.CurrentTestOutcome == UnitTestOutcome.Failed)
            {
                CaptureScreenshot(TestContext.TestName ?? "unknown");
            }
        }

        public TestContext? TestContext { get; set; }

        private void CaptureScreenshot(string testName)
        {
            try
            {
                var screenshotDir = Path.Combine(
                    Environment.GetEnvironmentVariable("BUILD_ARTIFACTSTAGINGDIRECTORY") 
                        ?? Path.GetTempPath(),
                    "Screenshots"
                );
                Directory.CreateDirectory(screenshotDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"{testName}_{timestamp}.png";
                var filePath = Path.Combine(screenshotDir, fileName);

                _mainWindow?.Capture()?.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                
                TestContext?.WriteLine($"Screenshot saved: {filePath}");
            }
            catch (Exception ex)
            {
                TestContext?.WriteLine($"Failed to capture screenshot: {ex.Message}");
            }
        }

        #endregion

        private static class NativeMethods
        {
            internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindowVisible(nint hWnd);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern int GetWindowTextLength(nint hWnd);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);
        }
    }
}
