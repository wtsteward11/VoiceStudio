using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap048Tests
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

  private static string EffectsMixerViewXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "EffectsMixerView.xaml");

  private static string EffectsMixerViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "EffectsMixerViewModel.cs");

  [TestMethod]
  public void EffectsMixerView_HasStudioSoundButton()
  {
    var text = File.ReadAllText(EffectsMixerViewXamlPath);
    StringAssert.Contains(text, "EffectsMixerView_StudioSoundButton");
  }

  [TestMethod]
  public void EffectsMixerViewModel_HasRunStudioSoundCommand_InSource()
  {
    var text = File.ReadAllText(EffectsMixerViewModelPath);
    StringAssert.Contains(text, "RunStudioSoundCommand");
  }

  [TestMethod]
  public void EffectsMixerViewModel_HasStudioSoundEffects_InSource()
  {
    var text = File.ReadAllText(EffectsMixerViewModelPath);
    StringAssert.Contains(text, "StudioSoundEffects");
  }

  [TestMethod]
  public void EffectsMixerViewModel_HasIsStudioSoundRunning_InSource()
  {
    var text = File.ReadAllText(EffectsMixerViewModelPath);
    StringAssert.Contains(text, "IsStudioSoundRunning");
  }

  [TestMethod]
  public void EffectsMixerViewModel_HasStudioSoundOutputAudioId_InSource()
  {
    var text = File.ReadAllText(EffectsMixerViewModelPath);
    StringAssert.Contains(text, "StudioSoundOutputAudioId");
  }

  [TestMethod]
  public void EffectsMixerViewModel_UsesCreateEffectChainAsync_InSource()
  {
    var text = File.ReadAllText(EffectsMixerViewModelPath);
    StringAssert.Contains(text, "CreateEffectChainAsync");
  }

  [TestMethod]
  public void EffectsMixerViewModel_UsesProcessAudioWithChainAsync_InSource()
  {
    var text = File.ReadAllText(EffectsMixerViewModelPath);
    StringAssert.Contains(text, "ProcessAudioWithChainAsync");
  }

  [TestMethod]
  public void EffectsMixerViewModel_HasCanRunStudioSound_InSource()
  {
    var text = File.ReadAllText(EffectsMixerViewModelPath);
    StringAssert.Contains(text, "CanRunStudioSound");
  }
}
