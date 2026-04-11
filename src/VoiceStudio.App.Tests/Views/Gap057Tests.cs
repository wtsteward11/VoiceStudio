using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap057Tests
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

  private static string StsMarkingModelsPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Core", "Models", "StsMarkingModels.cs");

  private static string ISpeechToSpeechServicePath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "ISpeechToSpeechService.cs");

  private static string IBackendClientPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Core", "Services", "IBackendClient.cs");

  private static string SpeechToSpeechViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "SpeechToSpeechViewModel.cs");

  private static string SpeechToSpeechViewXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "SpeechToSpeechView.xaml");

  [TestMethod]
  public void StsMarkingModels_HasIsTransformedField()
  {
    var text = File.ReadAllText(StsMarkingModelsPath);
    StringAssert.Contains(text, "IsTransformed");
    StringAssert.Contains(text, "is_transformed");
  }

  [TestMethod]
  public void StsMarkingModels_HasTransformationTypeField()
  {
    var text = File.ReadAllText(StsMarkingModelsPath);
    StringAssert.Contains(text, "TransformationType");
    StringAssert.Contains(text, "transformation_type");
  }

  [TestMethod]
  public void StsMarkingModels_HasSourceReferenceIdField()
  {
    var text = File.ReadAllText(StsMarkingModelsPath);
    StringAssert.Contains(text, "SourceReferenceId");
    StringAssert.Contains(text, "source_reference_id");
  }

  [TestMethod]
  public void ISpeechToSpeechService_HasGetMarkingAsync()
  {
    var text = File.ReadAllText(ISpeechToSpeechServicePath);
    StringAssert.Contains(text, "GetMarkingAsync");
  }

  [TestMethod]
  public void IBackendClient_HasGetStsMarkingAsync()
  {
    var text = File.ReadAllText(IBackendClientPath);
    StringAssert.Contains(text, "GetStsMarkingAsync");
  }

  [TestMethod]
  public void SpeechToSpeechViewModel_HasOutputMarkingVerified()
  {
    var text = File.ReadAllText(SpeechToSpeechViewModelPath);
    StringAssert.Contains(text, "outputMarkingVerified");
  }

  [TestMethod]
  public void SpeechToSpeechView_Xaml_HasMarkingBadge()
  {
    var text = File.ReadAllText(SpeechToSpeechViewXamlPath);
    StringAssert.Contains(text, "SpeechToSpeechView_MarkingBadge");
  }
}
