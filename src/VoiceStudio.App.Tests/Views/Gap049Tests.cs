using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap049Tests
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

  private static string VoiceSynthesisViewXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "VoiceSynthesisView.xaml");

  private static string VoiceSynthesisViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "VoiceSynthesisViewModel.cs");

  private static string IVoiceSynthesisServicePath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "IVoiceSynthesisService.cs");

  [TestMethod]
  public void VoiceSynthesisView_HasLongFormToggle()
  {
    var text = File.ReadAllText(VoiceSynthesisViewXamlPath);
    StringAssert.Contains(text, "VoiceSynthesisView_LongFormToggle");
  }

  [TestMethod]
  public void VoiceSynthesisView_HasLongFormProgressText()
  {
    var text = File.ReadAllText(VoiceSynthesisViewXamlPath);
    StringAssert.Contains(text, "VoiceSynthesisView_LongFormProgressText");
  }

  [TestMethod]
  public void VoiceSynthesisViewModel_HasUseLongForm_InSource()
  {
    var text = File.ReadAllText(VoiceSynthesisViewModelPath);
    StringAssert.Contains(text, "useLongForm");
  }

  [TestMethod]
  public void VoiceSynthesisViewModel_HasSynthesizeLongFormAsync_InSource()
  {
    var text = File.ReadAllText(VoiceSynthesisViewModelPath);
    StringAssert.Contains(text, "SynthesizeLongFormAsync");
  }

  [TestMethod]
  public void VoiceSynthesisViewModel_HasLongFormProgressText_InSource()
  {
    var text = File.ReadAllText(VoiceSynthesisViewModelPath);
    StringAssert.Contains(text, "longFormProgressText");
  }

  [TestMethod]
  public void IVoiceSynthesisService_HasSynthesizeLongFormAsync_InSource()
  {
    var text = File.ReadAllText(IVoiceSynthesisServicePath);
    StringAssert.Contains(text, "SynthesizeLongFormAsync");
  }
}
