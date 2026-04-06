using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Core
{
  /// <summary>GAP-053: Engine priority stays on ISettingsClient / settings API, not IBackendClient.</summary>
  [TestClass]
  public class IBackendClientEnginePriorityBoundaryTests
  {
    [TestMethod]
    public void IBackendClient_DoesNotExposeEnginePriorityMethods()
    {
      foreach (var m in typeof(IBackendClient).GetMethods(BindingFlags.Public | BindingFlags.Instance))
      {
        Assert.IsFalse(
            m.Name.Contains("EnginePriority", StringComparison.OrdinalIgnoreCase),
            $"IBackendClient must not expose engine priority surface: {m.Name}");
      }
    }
  }
}
