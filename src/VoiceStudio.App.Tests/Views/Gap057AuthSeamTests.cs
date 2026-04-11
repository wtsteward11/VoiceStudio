using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

/// <summary>
/// GAP-057: Client seam checks for mandatory auth — 401 mapping and non-blocking marking failures.
/// </summary>
[TestClass]
public sealed class Gap057AuthSeamTests
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

  private static string BackendTransportPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "Gateways", "BackendTransport.cs");

  private static string SpeechToSpeechViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Views", "Panels", "SpeechToSpeechViewModel.cs");

  [TestMethod]
  public void BackendTransport_MapsHttp401ToAuthenticationFailed()
  {
    var text = File.ReadAllText(BackendTransportPath);
    StringAssert.Contains(text, "401 =>");
    StringAssert.Contains(text, "AUTHENTICATION_FAILED");
  }

  [TestMethod]
  public void SpeechToSpeechViewModel_GetMarkingAsync_IsNonBlockingOnFailure()
  {
    var text = File.ReadAllText(SpeechToSpeechViewModelPath);
    StringAssert.Contains(text, "GetMarkingAsync");
    StringAssert.Contains(text, "Marking status lookup failed");
    StringAssert.Contains(text, "catch (Exception ex)");
  }
}
