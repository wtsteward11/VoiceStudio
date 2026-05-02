using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap067Slice4Tests
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

    throw new InvalidOperationException("VoiceStudio.sln not found.");
  }

  private static string FileActivationPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "FileActivation.cs");

  private static string FileActivationArgsPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "FileActivationArgs.cs");

  private static string AppXamlPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "App.xaml.cs");

  private static string MainWindowPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.xaml.cs");

  private static string JumpListActivationPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "JumpListActivation.cs");

  private static string JsonProjectRepositoryPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "JsonProjectRepository.cs");

  private static string InnoPath =>
    Path.Combine(FindRepoRoot(), "installer", "VoiceStudio.iss");

  [TestMethod]
  public void FileActivation_Exists()
  {
    var text = File.ReadAllText(FileActivationPath);
    StringAssert.Contains(text, "public static class FileActivation");
    StringAssert.Contains(text, "TryParse");
  }

  [TestMethod]
  public void FileActivation_TryParse_ReturnsNull_ForEmptyArgs()
  {
    Assert.IsNull(FileActivation.TryParse(null, new[] { "only.exe" }));
  }

  [TestMethod]
  public void FileActivation_TryParse_ReturnsNull_ForJumpListFlags()
  {
    Assert.IsNull(FileActivation.TryParse(null, new[] { "VoiceStudio.App.exe", "--jumplist-new" }));
  }

  [TestMethod]
  public void FileActivation_TryParse_ReturnsOpenProject_ForVoiceprojPath()
  {
    var r = FileActivation.TryParse(null, new[] { "e.exe", @"C:\data\sample.voiceproj" });
    Assert.IsNotNull(r);
    Assert.AreEqual(FileActivationKind.OpenProject, r.Kind);
    Assert.IsTrue(r.FilePath.EndsWith("sample.voiceproj", StringComparison.OrdinalIgnoreCase));
  }

  [TestMethod]
  public void FileActivation_TryParse_ReturnsImportProject_ForVstudioPath()
  {
    var r = FileActivation.TryParse(null, new[] { "e.exe", @"D:\share\proj.vstudio" });
    Assert.IsNotNull(r);
    Assert.AreEqual(FileActivationKind.ImportProject, r.Kind);
  }

  [TestMethod]
  public void FileActivation_TryParse_ReturnsImportProfile_ForVprofilePath()
  {
    var r = FileActivation.TryParse(null, new[] { "e.exe", @"D:\p\my.vprofile" });
    Assert.IsNotNull(r);
    Assert.AreEqual(FileActivationKind.ImportProfile, r.Kind);
  }

  [TestMethod]
  public void FileActivation_TryParse_HandlesQuotedPaths()
  {
    var r = FileActivation.TryParse(
      "\"C:\\temp\\spaced file.voiceproj\"",
      new[] { "e.exe", "ignored" });
    Assert.IsNotNull(r);
    Assert.AreEqual(FileActivationKind.OpenProject, r.Kind);
    Assert.IsTrue(r.FilePath.EndsWith("spaced file.voiceproj", StringComparison.OrdinalIgnoreCase));
  }

  [TestMethod]
  public void FileActivation_TryParse_ReturnsNull_ForUnrecognizedExtension()
  {
    Assert.IsNull(FileActivation.TryParse(null, new[] { "e.exe", @"C:\x\readme.txt" }));
  }

  [TestMethod]
  public void FileActivationArgs_RecognizedExtensions_ContainsExpected()
  {
    CollectionAssert.AreEquivalent(
      new[] { ".voiceproj", ".vstudio", ".vprofile" },
      FileActivationArgs.RecognizedExtensions.ToArray());
  }

  [TestMethod]
  public void App_OnLaunched_Wires_FileActivation_SetPendingIfParsed()
  {
    var text = File.ReadAllText(AppXamlPath);
    StringAssert.Contains(text, "FileActivation.SetPendingIfParsed");
    StringAssert.Contains(text, "JumpListActivation.HasPending");
  }

  [TestMethod]
  public void MainWindow_HasTryDispatchPendingFileActivation()
  {
    var text = File.ReadAllText(MainWindowPath);
    StringAssert.Contains(text, "_fileActivationShellBridge");
    StringAssert.Contains(text, "TryDispatchPendingFileActivation");
    var bridgePath = Path.Combine(
      FindRepoRoot(),
      "src",
      "VoiceStudio.App",
      "Services",
      "MainWindowFileActivationShellBridge.cs");
    var bridgeText = File.ReadAllText(bridgePath);
    StringAssert.Contains(bridgeText, "RunFileActivationPendingAsync");
  }

  [TestMethod]
  public void JumpListActivation_Declares_HasPending()
  {
    var text = File.ReadAllText(JumpListActivationPath);
    StringAssert.Contains(text, "HasPending");
  }

  [TestMethod]
  public void JsonProjectRepository_Declares_OpenProjectFileAsync()
  {
    var text = File.ReadAllText(JsonProjectRepositoryPath);
    StringAssert.Contains(text, "OpenProjectFileAsync");
  }

  [TestMethod]
  public void InnoSetup_Registers_Vstudio_Association()
  {
    var text = File.ReadAllText(InnoPath);
    StringAssert.Contains(text, ".vstudio");
    StringAssert.Contains(text, "VoiceStudio.Collaboration");
  }
}
