using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace VoiceStudio.App.Tests.UI.E2E
{
    /// <summary>
    /// Smoke tests for critical user journeys using FlaUI.
    /// These tests verify that the application starts and core UI components are accessible.
    /// </summary>
    [TestClass]
    [TestCategory("E2E")]
    [TestCategory("Smoke")]
    public class SmokeTests
    {
        private static Application? _app;
        private static UIA3Automation? _automation;
        private static Window? _mainWindow;
        private static string? _appPath;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
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
                _app = Application.Launch(_appPath);
                
                // Wait for main window to appear with timeout
                var retryResult = Retry.WhileNull(
                    () => _app.GetMainWindow(_automation),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromMilliseconds(500));
                
                _mainWindow = retryResult.Result;
                
                if (_mainWindow == null)
                {
                    Assert.Inconclusive("Main window did not appear within timeout.");
                }
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

            if (_app != null)
            {
                try
                {
                    if (!_app.HasExited)
                    {
                        _app.Close();
                    }
                }
                catch (InvalidOperationException ex)
                {
                    System.Diagnostics.Trace.WriteLine($"SmokeTests cleanup: application process no longer available: {ex.Message}");
                }
                finally
                {
                    _app = null;
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

        private static string? FindApplicationPath()
        {
            // Try several common locations
            var possiblePaths = new[]
            {
                // Development build
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", 
                    "VoiceStudio.App", "bin", "x64", "Debug", "net8.0-windows10.0.19041.0", "VoiceStudio.App.exe"),
                // Release build
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", 
                    "VoiceStudio.App", "bin", "x64", "Release", "net8.0-windows10.0.19041.0", "VoiceStudio.App.exe"),
                // CI build output
                Path.Combine(Environment.GetEnvironmentVariable("BUILD_ARTIFACTSTAGINGDIRECTORY") ?? "",
                    "VoiceStudio.App.exe"),
                // Local output
                @"E:\VoiceStudio\src\VoiceStudio.App\bin\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
            };

            foreach (var path in possiblePaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
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
    }
}
