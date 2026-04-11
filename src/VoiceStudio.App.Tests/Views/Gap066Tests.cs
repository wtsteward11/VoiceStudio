using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap066Tests
{
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

  private static string DesignTokensPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Resources", "DesignTokens.xaml");

  private static string MainWindowXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.xaml");

  private static string HelpOverlayXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Controls", "HelpOverlay.xaml");

  private static string FirstRunWizardXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "FirstRunWizard.xaml");

  private static string KeyboardCustomizationXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "KeyboardCustomizationView.xaml");

  [TestMethod]
  public void DesignTokens_HasShellMinWidthToken()
  {
    var text = File.ReadAllText(DesignTokensPath);
    StringAssert.Contains(text, "VSQ.Shell.MinWidth");
  }

  [TestMethod]
  public void DesignTokens_HasShellMinHeightToken()
  {
    var text = File.ReadAllText(DesignTokensPath);
    StringAssert.Contains(text, "VSQ.Shell.MinHeight");
  }

  [TestMethod]
  public void DesignTokens_HasPanelHostMinWidthToken()
  {
    var text = File.ReadAllText(DesignTokensPath);
    StringAssert.Contains(text, "VSQ.PanelHost.MinWidth");
  }

  [TestMethod]
  public void DesignTokens_HasHelpOverlayBackgroundToken()
  {
    var text = File.ReadAllText(DesignTokensPath);
    StringAssert.Contains(text, "VSQ.HelpOverlay.BackgroundBrush");
  }

  [TestMethod]
  public void MainWindow_RootGrid_HasMinSizeTokenReference()
  {
    var text = File.ReadAllText(MainWindowXamlPath);
    StringAssert.Contains(text, "VSQ.Shell.MinWidth");
    StringAssert.Contains(text, "VSQ.Shell.MinHeight");
  }

  [TestMethod]
  public void HelpOverlay_HasNoRawHexBackgroundColor()
  {
    var text = File.ReadAllText(HelpOverlayXamlPath);
    Assert.IsFalse(text.Contains("Background=\"#CC000000\"", StringComparison.Ordinal),
        "HelpOverlay must use VSQ.HelpOverlay.BackgroundBrush token, not raw hex.");
    StringAssert.Contains(text, "VSQ.HelpOverlay.BackgroundBrush");
  }

  [TestMethod]
  public void FirstRunWizard_HasContextualHelpButton()
  {
    var text = File.ReadAllText(FirstRunWizardXamlPath);
    StringAssert.Contains(text, "FirstRunWizard_HelpButton");
  }

  [TestMethod]
  public void KeyboardCustomizationView_HasContextualHelpButton()
  {
    var text = File.ReadAllText(KeyboardCustomizationXamlPath);
    StringAssert.Contains(text, "KeyboardCustomization_HelpButton");
  }
}
