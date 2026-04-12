using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

/// <summary>
/// GAP-067 slice 5 source-contract tests: progressive disclosure wiring and stable AutomationIds.
/// </summary>
[TestClass]
public sealed class Gap067Slice5Tests
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
  public void MainWindow_StatusBar_SystemMetrics_Disclosure()
  {
    var text = ReadAppFile("MainWindow.xaml");
    StringAssert.Contains(text, "MainWindow_StatusBar_SystemMetricsButton");
    StringAssert.Contains(text, "MainWindow_StatusBar_SystemMetricsFlyout");
  }

  [TestMethod]
  public void CustomizableToolbar_PerformanceOverflow_Disclosure()
  {
    var text = ReadAppFile("Controls", "CustomizableToolbar.xaml");
    StringAssert.Contains(text, "CustomizableToolbar_PerformanceOverflowButton");
    StringAssert.Contains(text, "CustomizableToolbar_PerformanceFlyout");
  }

  [TestMethod]
  public void VoiceSynthesisView_AdvancedExpander_Disclosure()
  {
    var text = ReadAppFile("Views", "Panels", "VoiceSynthesisView.xaml");
    StringAssert.Contains(text, "VoiceSynthesisView_AdvancedControlsExpander");
    StringAssert.Contains(text, "IsAdvancedSynthesisControlsExpanded");
  }

  [TestMethod]
  public void VoiceSynthesisViewModel_Persists_AdvancedExpanded_Key()
  {
    var text = ReadAppFile("Views", "Panels", "VoiceSynthesisViewModel.cs");
    StringAssert.Contains(text, "isAdvancedSynthesisControlsExpanded");
    StringAssert.Contains(text, "VoiceSynthesis_AdvancedControlsExpanded");
  }

  [TestMethod]
  public void TimelineView_TransportOverflow_Disclosure()
  {
    var text = ReadAppFile("Views", "Panels", "TimelineView.xaml");
    StringAssert.Contains(text, "TimelineView_TransportMoreButton");
    StringAssert.Contains(text, "TimelineView_AddTrackButton");
  }

  [TestMethod]
  public void TranscribeView_AdvancedExpander_Disclosure()
  {
    var text = ReadAppFile("Views", "Panels", "TranscribeView.xaml");
    StringAssert.Contains(text, "TranscribeView_AdvancedOptionsExpander");
    StringAssert.Contains(text, "IsAdvancedTranscribeOptionsExpanded");
  }

  [TestMethod]
  public void TranscribeViewModel_AdvancedExpanded_Property()
  {
    var text = ReadAppFile("Views", "Panels", "TranscribeViewModel.cs");
    StringAssert.Contains(text, "isAdvancedTranscribeOptionsExpanded");
  }
}
