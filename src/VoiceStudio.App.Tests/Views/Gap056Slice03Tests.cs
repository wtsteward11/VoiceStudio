using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

/// <summary>
/// GAP-056 Slice 03: Sample-level watermark embedding with detection parity.
/// Verifies C# DTO, ViewModel, and seam surfaces reflect watermark fields.
/// </summary>
[TestClass]
public sealed class Gap056Slice03Tests
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
        if (File.Exists(Path.Combine(dir.FullName, "VoiceStudio.sln")))
        {
          return dir.FullName;
        }
      }
    }

    throw new InvalidOperationException("VoiceStudio.sln not found.");
  }

  private static string StsMarkingModelsPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Core", "Models", "StsMarkingModels.cs");

  private static string ViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "SpeechToSpeechViewModel.cs");

  // --- DTO field presence ---

  [TestMethod]
  public void StsMarkingModels_HasWatermarkAppliedField()
  {
    var text = File.ReadAllText(StsMarkingModelsPath);
    StringAssert.Contains(text, "WatermarkApplied");
    StringAssert.Contains(text, "watermark_applied");
  }

  [TestMethod]
  public void StsMarkingModels_HasWatermarkVerifiedField()
  {
    var text = File.ReadAllText(StsMarkingModelsPath);
    StringAssert.Contains(text, "WatermarkVerified");
    StringAssert.Contains(text, "watermark_verified");
  }

  [TestMethod]
  public void StsMarkingModels_HasWatermarkMethodField()
  {
    var text = File.ReadAllText(StsMarkingModelsPath);
    StringAssert.Contains(text, "WatermarkMethod");
    StringAssert.Contains(text, "watermark_method");
  }

  // --- ViewModel observable properties ---

  [TestMethod]
  public void ViewModel_HasOutputWatermarkApplied()
  {
    var text = File.ReadAllText(ViewModelPath);
    StringAssert.Contains(text, "outputWatermarkApplied");
    StringAssert.Contains(text, "OutputWatermarkApplied");
  }

  [TestMethod]
  public void ViewModel_HasOutputWatermarkVerified()
  {
    var text = File.ReadAllText(ViewModelPath);
    StringAssert.Contains(text, "outputWatermarkVerified");
    StringAssert.Contains(text, "OutputWatermarkVerified");
  }

  [TestMethod]
  public void ViewModel_HasOutputWatermarkMethod()
  {
    var text = File.ReadAllText(ViewModelPath);
    StringAssert.Contains(text, "outputWatermarkMethod");
    StringAssert.Contains(text, "OutputWatermarkMethod");
  }

  // --- DTO type-level reflection ---

  [TestMethod]
  public void StsMarkingStatus_Type_HasWatermarkProperties()
  {
    var type = typeof(VoiceStudio.Core.Models.StsMarkingStatus);
    Assert.IsNotNull(type.GetProperty("WatermarkApplied"), "Missing WatermarkApplied property");
    Assert.IsNotNull(type.GetProperty("WatermarkVerified"), "Missing WatermarkVerified property");
    Assert.IsNotNull(type.GetProperty("WatermarkMethod"), "Missing WatermarkMethod property");
  }

  [TestMethod]
  public void StsMarkingStatus_WatermarkVerified_IsNullableBool()
  {
    var prop = typeof(VoiceStudio.Core.Models.StsMarkingStatus).GetProperty("WatermarkVerified");
    Assert.IsNotNull(prop);
    Assert.AreEqual(typeof(bool?), prop!.PropertyType);
  }

  [TestMethod]
  public void StsMarkingStatus_DefaultValues_AreHonest()
  {
    var status = new VoiceStudio.Core.Models.StsMarkingStatus();
    Assert.IsFalse(status.WatermarkApplied, "Default WatermarkApplied must be false");
    Assert.IsNull(status.WatermarkVerified, "Default WatermarkVerified must be null (unchecked)");
    Assert.IsNull(status.WatermarkMethod, "Default WatermarkMethod must be null");
  }
}
