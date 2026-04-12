using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

/// <summary>
/// GAP-067 slice 6 source-contract tests: WCAG 2.1 AA accessible naming and AutomationId presence
/// on all progressive-disclosure surfaces modified in Slice 5.
/// </summary>
[TestClass]
public sealed class Gap067Slice6Tests
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

  private static string ReadAppFile(params string[] relative)
  {
    var path = Path.Combine(new[] { FindRepoRoot(), "src", "VoiceStudio.App" }.Concat(relative).ToArray());
    return File.ReadAllText(path);
  }

  [TestMethod]
  public void StatusBar_SystemMetrics_HasAccessibleName()
  {
    var text = ReadAppFile("MainWindow.xaml");
    StringAssert.Contains(text, "AutomationProperties.Name=\"System metrics\"");
    StringAssert.Contains(text, "MainWindow_StatusBar_CollaboratorsButton");
    StringAssert.Contains(text, "AutomationProperties.Name=\"Collaborators\"");
  }

  [TestMethod]
  public void Toolbar_PerformanceOverflow_HasAccessibleName()
  {
    var text = ReadAppFile("Controls", "CustomizableToolbar.xaml");
    StringAssert.Contains(text, "AutomationProperties.Name=\"Performance metrics\"");
  }

  [TestMethod]
  public void VoiceSynthesis_Expander_HasAccessibleName()
  {
    var text = ReadAppFile("Views", "Panels", "VoiceSynthesisView.xaml");
    StringAssert.Contains(text, "AutomationProperties.Name=\"Advanced synthesis controls\"");
  }

  [TestMethod]
  public void VoiceSynthesis_Sliders_HaveLabeledBy()
  {
    var text = ReadAppFile("Views", "Panels", "VoiceSynthesisView.xaml");
    foreach (var label in new[] { "SpeedLabel", "PitchShiftLabel", "StabilityLabel", "ClarityLabel", "TemperatureLabel" })
    {
      StringAssert.Contains(text, $"x:Name=\"{label}\"", $"Missing x:Name for {label}");
      StringAssert.Contains(text, $"LabeledBy=\"{{x:Bind {label}}}\"", $"Missing LabeledBy for {label}");
    }
  }

  [TestMethod]
  public void Timeline_TransportMore_HasAccessibleName()
  {
    var text = ReadAppFile("Views", "Panels", "TimelineView.xaml");
    StringAssert.Contains(text, "AutomationProperties.Name=\"More transport options\"");
  }

  [TestMethod]
  public void Timeline_PlayStop_HaveAccessibleNames()
  {
    var text = ReadAppFile("Views", "Panels", "TimelineView.xaml");
    StringAssert.Contains(text, "AutomationProperties.Name=\"Play\"");
    StringAssert.Contains(text, "AutomationProperties.Name=\"Stop\"");
    StringAssert.Contains(text, "TimelineView_PlayButton");
    StringAssert.Contains(text, "TimelineView_StopButton");
  }

  [TestMethod]
  public void Timeline_FlyoutControls_HaveAutomationIds()
  {
    var text = ReadAppFile("Views", "Panels", "TimelineView.xaml");
    StringAssert.Contains(text, "TimelineView_OpenRecordingButton");
    StringAssert.Contains(text, "TimelineView_LoopToggle");
    StringAssert.Contains(text, "TimelineView_ZoomInButton");
    StringAssert.Contains(text, "TimelineView_ZoomOutButton");
  }

  [TestMethod]
  public void Transcribe_Expander_HasAccessibleName()
  {
    var text = ReadAppFile("Views", "Panels", "TranscribeView.xaml");
    StringAssert.Contains(text, "AutomationProperties.Name=\"Advanced transcription options\"");
  }
}
