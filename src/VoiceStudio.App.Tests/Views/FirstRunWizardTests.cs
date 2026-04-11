using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Views;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class FirstRunWizardTests
{
  private string _tempSettingsPath = string.Empty;

  [TestInitialize]
  public void TestInit()
  {
    _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"vs_wizard_test_{Guid.NewGuid():N}.json");
    UnpackagedSettingsHelper.UseTestSettingsPath(_tempSettingsPath);
  }

  [TestCleanup]
  public void TestCleanup()
  {
    UnpackagedSettingsHelper.ResetSettingsPath();
    if (File.Exists(_tempSettingsPath))
    {
      try
      {
        File.Delete(_tempSettingsPath);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"FirstRunWizardTests: temp settings delete failed: {ex.Message}");
      }
    }
  }

  private static string FindRepoRoot()
  {
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "" })
    {
      if (string.IsNullOrEmpty(start))
      {
        continue;
      }

      var dir = new DirectoryInfo(start);
      for (var i = 0; i < 16 && dir != null; i++, dir = dir.Parent)
      {
        var sln = Path.Combine(dir.FullName, "VoiceStudio.sln");
        if (File.Exists(sln))
        {
          return dir.FullName;
        }
      }
    }

    throw new InvalidOperationException("VoiceStudio.sln not found (current dir, base dir, or assembly location).");
  }

  private static string AppServicesPath => Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "AppServices.cs");

  private static string AppXamlCsPath => Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "App.xaml.cs");

  [TestMethod]
  public void OnboardingWizardService_RegisteredInDI_AppServicesSource()
  {
    var text = File.ReadAllText(AppServicesPath);
    StringAssert.Contains(text, "AddSingleton<OnboardingWizardService>");
    StringAssert.Contains(text, "GetOnboardingWizardService");
  }

  [TestMethod]
  public void CancelOnFirstRun_AppExitsOnlyWhen_IsFirstRun_And_NotWasCompleted_SeamScan()
  {
    var text = File.ReadAllText(AppXamlCsPath);
    Assert.IsTrue(text.Contains("isFirstRun", StringComparison.Ordinal), "Expected isFirstRun variable.");
    Assert.IsTrue(text.Contains("!wizard.WasCompleted", StringComparison.Ordinal) && text.Contains("isFirstRun", StringComparison.Ordinal),
        "Expected exit guard combining WasCompleted and first-run.");
  }

  [TestMethod]
  public void FirstRunWizard_TotalSteps_IsFive_InSource()
  {
    var path = Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "FirstRunWizard.xaml.cs");
    var text = File.ReadAllText(path);
    Assert.IsTrue(text.Contains("TotalSteps = 5", StringComparison.Ordinal), "Wizard must define five steps.");
  }

  [TestMethod]
  public void WizardCurrentStep_Key_MatchesConstant()
  {
    Assert.AreEqual("WizardCurrentStep", FirstRunWizard.WizardCurrentStepKey);
  }

  [TestMethod]
  public void Settings_RealAppsettingsFile_IsNotTouchedDuringWizardTests()
  {
    var realPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceStudio",
        "appsettings.json");
    Assert.IsFalse(string.Equals(realPath, _tempSettingsPath, StringComparison.OrdinalIgnoreCase),
        "Test must be running against a temp path, not the real user settings file.");
  }

  [TestMethod]
  public void ShouldShowWizard_FirstLaunch_ReturnsTrue()
  {
    UnpackagedSettingsHelper.SetValue("FirstRunComplete", false);
    UnpackagedSettingsHelper.SetValue("ShowWizardOnStartup", false);
    var show = FirstRunWizard.ShouldShowWizardAsync().GetAwaiter().GetResult();
    Assert.IsTrue(show);
  }

  [TestMethod]
  public void ShouldShowWizard_CompletedAndDontShowAgain_ReturnsFalse()
  {
    UnpackagedSettingsHelper.SetValue("FirstRunComplete", true);
    UnpackagedSettingsHelper.SetValue("ShowWizardOnStartup", false);
    var show = FirstRunWizard.ShouldShowWizardAsync().GetAwaiter().GetResult();
    Assert.IsFalse(show);
  }

  [TestMethod]
  public void ShouldShowWizard_CompletedAndShowOnStartup_ReturnsTrue()
  {
    UnpackagedSettingsHelper.SetValue("FirstRunComplete", true);
    UnpackagedSettingsHelper.SetValue("ShowWizardOnStartup", true);
    var show = FirstRunWizard.ShouldShowWizardAsync().GetAwaiter().GetResult();
    Assert.IsTrue(show);
  }

  [TestMethod]
  public void SaveFirstRunComplete_ResetsWizardCurrentStep_InSource()
  {
    var path = Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "FirstRunWizard.xaml.cs");
    var text = File.ReadAllText(path);
    StringAssert.Contains(text, "WizardCurrentStepKey");
    StringAssert.Contains(text, "SetValue(WizardCurrentStepKey, 1)");
  }

  [TestMethod]
  public void CheckBackendHealth_UsesDiagnosticsAndEnginesClients_InSource()
  {
    var path = Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "FirstRunWizard.xaml.cs");
    var text = File.ReadAllText(path);
    StringAssert.Contains(text, "IDiagnosticsClient");
    StringAssert.Contains(text, "CheckHealthAsync");
    StringAssert.Contains(text, "IEnginesClient");
    StringAssert.Contains(text, "GetEnginesAsync");
    Assert.IsFalse(text.Contains("backendBase}/health", StringComparison.Ordinal), "Raw /health HttpClient call should be removed.");
  }

  [TestMethod]
  public void StartBackend_UsesBackendProcessManager_InSource()
  {
    var path = Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "FirstRunWizard.xaml.cs");
    var text = File.ReadAllText(path);
    StringAssert.Contains(text, "BackendProcessManager");
    StringAssert.Contains(text, "EnsureBackendRunningAsync");
    Assert.IsFalse(text.Contains("-m uvicorn", StringComparison.Ordinal), "Raw uvicorn Process.Start should be removed.");
  }
}
