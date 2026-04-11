using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap052Tests
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

  private static string QualityBenchmarkViewXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "QualityBenchmarkView.xaml");

  private static string QualityBenchmarkViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "QualityBenchmarkViewModel.cs");

  [TestMethod]
  public void QualityBenchmarkView_HasRunComparisonButton()
  {
    var text = File.ReadAllText(QualityBenchmarkViewXamlPath);
    StringAssert.Contains(text, "QualityBenchmarkView_RunComparisonButton");
  }

  [TestMethod]
  public void QualityBenchmarkView_HasComparisonSlotsList()
  {
    var text = File.ReadAllText(QualityBenchmarkViewXamlPath);
    StringAssert.Contains(text, "QualityBenchmarkView_ComparisonSlots");
  }

  [TestMethod]
  public void QualityBenchmarkViewModel_UsesIEnginesClient_InSource()
  {
    var text = File.ReadAllText(QualityBenchmarkViewModelPath);
    StringAssert.Contains(text, "IEnginesClient");
  }

  [TestMethod]
  public void QualityBenchmarkViewModel_UsesIVoiceSynthesisService_InSource()
  {
    var text = File.ReadAllText(QualityBenchmarkViewModelPath);
    StringAssert.Contains(text, "IVoiceSynthesisService");
  }

  [TestMethod]
  public void QualityBenchmarkViewModel_UsesIAudioPlayerService_InSource()
  {
    var text = File.ReadAllText(QualityBenchmarkViewModelPath);
    StringAssert.Contains(text, "IAudioPlayerService");
  }

  [TestMethod]
  public void QualityBenchmarkViewModel_HasComparisonSlotClass_InSource()
  {
    var text = File.ReadAllText(QualityBenchmarkViewModelPath);
    StringAssert.Contains(text, "ComparisonSlot");
  }

  [TestMethod]
  public void QualityBenchmarkViewModel_HasSubjectiveScore_InSource()
  {
    var text = File.ReadAllText(QualityBenchmarkViewModelPath);
    StringAssert.Contains(text, "SubjectiveScore");
  }

  [TestMethod]
  public void QualityBenchmarkViewModel_HasIsPreferred_InSource()
  {
    var text = File.ReadAllText(QualityBenchmarkViewModelPath);
    StringAssert.Contains(text, "IsPreferred");
  }
}
