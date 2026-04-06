using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Core
{
  /// <summary>GAP-062: Torch venv diagnostics stay on ISettingsClient / settings API, not IBackendClient.</summary>
  [TestClass]
  public class IBackendClientTorchVenvBoundaryTests
  {
    [TestMethod]
    public void IBackendClient_DoesNotExposeTorchVenvMethods()
    {
      foreach (var m in typeof(IBackendClient).GetMethods(BindingFlags.Public | BindingFlags.Instance))
      {
        Assert.IsFalse(
            m.Name.Contains("TorchVenv", StringComparison.OrdinalIgnoreCase),
            $"IBackendClient must not expose torch venv surface: {m.Name}");
      }
    }
  }
}
