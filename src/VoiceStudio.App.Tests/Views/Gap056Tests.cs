using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap056Tests
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
  public void SpeechToSpeechResponse_HasIsTransformedField()
  {
    var text = File.ReadAllText(SpeechToSpeechModelsPath);
    StringAssert.Contains(text, "IsTransformed");
    StringAssert.Contains(text, "is_transformed");
  }

  [TestMethod]
  public void SpeechToSpeechResponse_HasTransformationTypeField()
  {
    var text = File.ReadAllText(SpeechToSpeechModelsPath);
    StringAssert.Contains(text, "TransformationType");
    StringAssert.Contains(text, "transformation_type");
  }

  [TestMethod]
  public void SpeechToSpeechResponse_HasSourceAudioIdField()
  {
    var text = File.ReadAllText(SpeechToSpeechModelsPath);
    StringAssert.Contains(text, "SourceAudioId");
    StringAssert.Contains(text, "source_audio_id");
  }

  [TestMethod]
  public void SpeechToSpeechResponse_HasDisclosureTextField()
  {
    var text = File.ReadAllText(SpeechToSpeechModelsPath);
    StringAssert.Contains(text, "DisclosureText");
    StringAssert.Contains(text, "disclosure_text");
  }

  [TestMethod]
  public void SpeechToSpeechViewModel_HasOutputDisclosureTextProperty()
  {
    var text = File.ReadAllText(SpeechToSpeechViewModelPath);
    StringAssert.Contains(text, "outputDisclosureText");
  }

  [TestMethod]
  public void SpeechToSpeechView_Xaml_HasDisclosureTextAutomationId()
  {
    var text = File.ReadAllText(SpeechToSpeechViewXamlPath);
    StringAssert.Contains(text, "SpeechToSpeechView_DisclosureText");
  }
}
