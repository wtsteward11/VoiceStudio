using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>GAP-054: SSML diagnostics ride on synthesis response — no SSML-specific IBackendClient surface.</summary>
  [TestClass]
  public class IBackendClientSsmlBoundaryTests
  {
    [TestMethod]
    public void IBackendClient_HasNoSsmlNamedMethods()
    {
      var bad = typeof(IBackendClient).GetMethods(BindingFlags.Public | BindingFlags.Instance)
          .Where(m => m.Name.Contains("Ssml", System.StringComparison.OrdinalIgnoreCase))
          .Select(m => m.Name)
          .ToList();
      Assert.AreEqual(0, bad.Count, "Unexpected SSML-specific API creep: " + string.Join(", ", bad));
    }
  }
}
