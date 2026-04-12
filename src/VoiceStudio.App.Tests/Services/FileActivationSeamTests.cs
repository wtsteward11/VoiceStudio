using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
[TestCategory("Services")]
public sealed class FileActivationSeamTests
{
  [TestMethod]
  public void TryParse_BareVoiceprojPath_ReturnsOpenProject()
  {
    var r = FileActivation.TryParse(null, new[] { "VoiceStudio.App.exe", @"E:\projects\demo.voiceproj" });
    Assert.IsNotNull(r);
    Assert.AreEqual(FileActivationKind.OpenProject, r.Kind);
  }

  [TestMethod]
  public void TryParse_BareVstudioPath_ReturnsImportProject()
  {
    var r = FileActivation.TryParse(null, new[] { "VoiceStudio.App.exe", @"E:\collab\pack.vstudio" });
    Assert.IsNotNull(r);
    Assert.AreEqual(FileActivationKind.ImportProject, r.Kind);
  }

  [TestMethod]
  public void TryParse_BareVprofilePath_ReturnsImportProfile()
  {
    var r = FileActivation.TryParse(null, new[] { "VoiceStudio.App.exe", @"E:\profiles\u1.vprofile" });
    Assert.IsNotNull(r);
    Assert.AreEqual(FileActivationKind.ImportProfile, r.Kind);
  }

  [TestMethod]
  public void TryParse_QuotedPathWithSpaces_ExtractsCorrectly()
  {
    var r = FileActivation.TryParse(
      "\"E:\\voice\\my project file.vprofile\"",
      new[] { "VoiceStudio.App.exe", "placeholder" });
    Assert.IsNotNull(r);
    Assert.AreEqual(FileActivationKind.ImportProfile, r.Kind);
    Assert.IsTrue(r.FilePath.EndsWith("my project file.vprofile", StringComparison.OrdinalIgnoreCase));
  }

  [TestMethod]
  public void TryParse_MultipleArgs_FirstFileWins()
  {
    var r = FileActivation.TryParse(null, new[]
    {
      "VoiceStudio.App.exe",
      "--flag",
      @"C:\first.voiceproj",
      @"C:\second.voiceproj",
    });
    Assert.IsNotNull(r);
    Assert.IsTrue(r.FilePath.EndsWith("first.voiceproj", StringComparison.OrdinalIgnoreCase));
  }

  [TestMethod]
  public void TryParse_NonExistentExtension_ReturnsNull()
  {
    Assert.IsNull(FileActivation.TryParse(null, new[] { "VoiceStudio.App.exe", @"C:\nope.docx" }));
  }
}
