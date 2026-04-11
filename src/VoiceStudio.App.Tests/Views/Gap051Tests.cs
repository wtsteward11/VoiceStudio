using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap051Tests
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

  private static string SpeechToSpeechViewXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "SpeechToSpeechView.xaml");

  private static string SpeechToSpeechViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "SpeechToSpeechViewModel.cs");

  private static string ISpeechToSpeechServicePath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "ISpeechToSpeechService.cs");

  [TestMethod]
  public void SpeechToSpeechView_HasSourceAudioAutomationId()
  {
    var text = File.ReadAllText(SpeechToSpeechViewXamlPath);
    StringAssert.Contains(text, "SpeechToSpeechView_SourceAudioSelector");
  }

  [TestMethod]
  public void SpeechToSpeechView_HasTargetVoiceAutomationId()
  {
    var text = File.ReadAllText(SpeechToSpeechViewXamlPath);
    StringAssert.Contains(text, "SpeechToSpeechView_TargetVoiceSelector");
  }

  [TestMethod]
  public void SpeechToSpeechView_HasConvertButtonAutomationId()
  {
    var text = File.ReadAllText(SpeechToSpeechViewXamlPath);
    StringAssert.Contains(text, "SpeechToSpeechView_ConvertButton");
  }

  [TestMethod]
  public void SpeechToSpeechView_HasStatusAndOutputAutomationIds()
  {
    var text = File.ReadAllText(SpeechToSpeechViewXamlPath);
    StringAssert.Contains(text, "SpeechToSpeechView_StatusText");
    StringAssert.Contains(text, "SpeechToSpeechView_OutputAudioLink");
  }

  [TestMethod]
  public void SpeechToSpeechViewModel_HasConvertCommand_InSource()
  {
    var text = File.ReadAllText(SpeechToSpeechViewModelPath);
    StringAssert.Contains(text, "ConvertCommand");
  }

  [TestMethod]
  public void ISpeechToSpeechService_HasConvertSpeechAsync_InSource()
  {
    var text = File.ReadAllText(ISpeechToSpeechServicePath);
    StringAssert.Contains(text, "ConvertSpeechAsync");
  }
}
