using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;

namespace VoiceStudio.App.UITests
{
    [TestClass]
    public class UiSmokeTests
    {
        private const string DefaultDriverUrl = "http://127.0.0.1:4723";

        [TestMethod]
        public void LaunchesMainWindow()
        {
            var appPath = Environment.GetEnvironmentVariable("VS_APP_PATH");
            if (string.IsNullOrWhiteSpace(appPath))
            {
                Assert.Inconclusive("Set VS_APP_PATH to the built VoiceStudio.App.exe before running UI smoke.");
            }

            var driverUrl = Environment.GetEnvironmentVariable("WINAPPDRIVER_URL") ?? DefaultDriverUrl;

            var options = new AppiumOptions();
            options.AddAdditionalCapability("app", appPath);
            options.AddAdditionalCapability("platformName", "Windows");
            options.AddAdditionalCapability("deviceName", "WindowsPC");

            using var session = new WindowsDriver<WindowsElement>(
                new Uri(driverUrl),
                options,
                TimeSpan.FromSeconds(60));

            Assert.IsNotNull(session, "WinAppDriver session should be created");
            Assert.IsFalse(string.IsNullOrWhiteSpace(session.SessionId), "SessionId should be assigned");

            var title = session.Title;
            Assert.IsNotNull(title, "Window title should be retrievable");
        }
    }
}
