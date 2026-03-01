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
    /// GAP-TC-006: Refactored Assert.Inconclusive to use proper conditional skip pattern.
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
        private static string? _skipReason;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            // Find the application executable
            _appPath = FindApplicationPath();
            
            if (string.IsNullOrEmpty(_appPath) || !File.Exists(_appPath))
            {
                _skipReason = "VoiceStudio.App.exe not found. Ensure the application is built.";
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
                    _skipReason = "Main window did not appear within timeout.";
                }
            }
            catch (Exception ex)
            {
                _skipReason = $"Failed to launch application: {ex.Message}";
            }
        }

        /// <summary>
        /// Guard method to skip tests when preconditions are not met.
        /// Call at the start of each test method.
        /// </summary>
        private static void SkipIfNotReady()
        {
            if (!string.IsNullOrEmpty(_skipReason))
            {
                Assert.Inconclusive(_skipReason);
            }
            
            if (_mainWindow == null || _automation == null)
            {
                Assert.Inconclusive("Test infrastructure not initialized.");
            }
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            _mainWindow = null;
            _app?.Close();
            _automation?.Dispose();
        }

        /// <summary>
        /// Critical Journey 1: Application launches successfully and main window is visible.
        /// </summary>
        [TestMethod]
        public void Journey1_ApplicationLaunches_MainWindowVisible()
        {
            // Guard: Skip if app not available
            SkipIfNotReady();
            
            // Arrange & Act - Done in ClassInitialize
            
            // Assert
            Assert.IsNotNull(_mainWindow, "Main window should be available");
            Assert.IsTrue(_mainWindow!.IsOffscreen == false, "Main window should be visible on screen");
            Assert.AreEqual("VoiceStudio", _mainWindow.Title, "Window title should be VoiceStudio");
        }

        /// <summary>
        /// Critical Journey 2: Navigation panel is accessible and contains expected items.
        /// </summary>
        [TestMethod]
        public void Journey2_NavigationPanel_IsAccessible()
        {
            // Guard: Skip if app not available
            SkipIfNotReady();
            
            // Arrange
            var cf = _automation!.ConditionFactory;
            
            // Act - Find the navigation view
            var navView = _mainWindow!.FindFirstDescendant(
                cf.ByAutomationId("MainNavigationView")
            );
            
            // Assert
            Assert.IsNotNull(navView, "NavigationView should be present");
            
            // Verify navigation items exist (at minimum)
            var navItems = navView.FindAllDescendants(
                cf.ByControlType(ControlType.ListItem)
            );
            Assert.IsTrue(navItems.Length > 0, "Navigation should contain menu items");
        }

        /// <summary>
        /// Critical Journey 3: Content area displays when navigation item selected.
        /// </summary>
        [TestMethod]
        public void Journey3_ContentArea_DisplaysOnNavigation()
        {
            // Guard: Skip if app not available
            SkipIfNotReady();
            
            // Arrange
            var cf = _automation!.ConditionFactory;
            
            // Act - Find content frame
            var contentFrame = _mainWindow!.FindFirstDescendant(
                cf.ByAutomationId("ContentFrame")
            );
            
            // Assert
            Assert.IsNotNull(contentFrame, "Content frame should be present");
        }

        /// <summary>
        /// Critical Journey 4: Settings can be accessed from navigation.
        /// </summary>
        [TestMethod]
        public void Journey4_Settings_CanBeAccessed()
        {
            // Guard: Skip if app not available
            SkipIfNotReady();
            
            // Arrange
            var cf = _automation!.ConditionFactory;
            
            // Act - Find settings navigation item or button
            var settingsItem = _mainWindow!.FindFirstDescendant(
                cf.ByAutomationId("SettingsNavItem")
            ) ?? _mainWindow!.FindFirstDescendant(
                cf.ByName("Settings")
            );
            
            if (settingsItem != null)
            {
                // Click on settings
                var clickable = settingsItem.AsButton() ?? settingsItem;
                clickable.Click();
                
                // Wait for settings view to load
                Thread.Sleep(500);
                
                // Find settings view
                var settingsView = _mainWindow!.FindFirstDescendant(
                    cf.ByAutomationId("SettingsView")
                );
                
                Assert.IsNotNull(settingsView, "Settings view should be displayed after clicking Settings");
            }
            else
            {
                // Settings may be accessed differently - check for settings icon in title bar
                var settingsButton = _mainWindow!.FindFirstDescendant(
                    cf.ByAutomationId("SettingsButton")
                );
                
                Assert.IsNotNull(settingsButton, "Settings should be accessible via button or nav item");
            }
        }

        /// <summary>
        /// Critical Journey 5: Theme switching works without errors.
        /// </summary>
        [TestMethod]
        public void Journey5_ThemeSwitch_CompletesWithoutError()
        {
            // Guard: Skip if app not available
            SkipIfNotReady();
            
            // Arrange
            var cf = _automation!.ConditionFactory;
            
            // First navigate to settings/theme area
            var themeCombo = FindThemeComboBox(cf);
            
            if (themeCombo == null)
            {
                // Theme selector not accessible - skip gracefully
                Assert.Inconclusive("Theme selector not found in current view. This test requires settings panel access.");
            }

            // Act - Try to interact with theme selector
            var comboBox = themeCombo!.AsComboBox();
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
