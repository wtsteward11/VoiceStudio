using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap055Tests
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

  private static string SpeechToSpeechModelsPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Core", "Models", "SpeechToSpeechModels.cs");

  private static string SpeechToSpeechViewXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "SpeechToSpeechView.xaml");

  private static string SpeechToSpeechViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "SpeechToSpeechViewModel.cs");

  [TestMethod]
  public void SpeechToSpeechRequest_HasConsentAcknowledgedProperty()
  {
    var text = File.ReadAllText(SpeechToSpeechModelsPath);
    StringAssert.Contains(text, "ConsentAcknowledged");
    StringAssert.Contains(text, "consent_acknowledged");
  }

  [TestMethod]
  public void SpeechToSpeechRequest_HasConsentIdProperty()
  {
    var text = File.ReadAllText(SpeechToSpeechModelsPath);
    StringAssert.Contains(text, "ConsentId");
    StringAssert.Contains(text, "consent_id");
  }

  [TestMethod]
  public void SpeechToSpeechViewModel_HasConsentAcknowledgedProperty()
  {
    var text = File.ReadAllText(SpeechToSpeechViewModelPath);
    StringAssert.Contains(text, "consentAcknowledged");
  }

  [TestMethod]
  public void SpeechToSpeechView_Xaml_HasConsentCheckBox()
  {
    var text = File.ReadAllText(SpeechToSpeechViewXamlPath);
    StringAssert.Contains(text, "SpeechToSpeechView_ConsentCheckBox");
    StringAssert.Contains(text, "ConsentAcknowledged");
  }

  [TestMethod]
  public void SpeechToSpeechViewModel_CanConvert_RequiresConsent()
  {
    var text = File.ReadAllText(SpeechToSpeechViewModelPath);
    StringAssert.Contains(text, "ConsentAcknowledged");
    StringAssert.Contains(text, "CanConvert");
  }
}
