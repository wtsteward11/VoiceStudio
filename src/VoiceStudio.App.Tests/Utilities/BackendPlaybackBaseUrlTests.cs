using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Utilities
{
  [TestClass]
  public sealed class BackendPlaybackBaseUrlTests
  {
    [TestMethod]
    public void Resolve_NullConfig_ReturnsBackendClientConfigDefault()
    {
      var url = BackendPlaybackBaseUrl.Resolve(null);
      Assert.AreEqual(BackendClientConfig.DefaultHttpBaseUrl, url);
    }

    [TestMethod]
    public void Resolve_EmptyBaseUrl_ReturnsDefault()
    {
      var url = BackendPlaybackBaseUrl.Resolve(new BackendClientConfig { BaseUrl = "" });
      Assert.AreEqual(BackendClientConfig.DefaultHttpBaseUrl, url);
    }

    [TestMethod]
    public void Resolve_TrimsTrailingSlash()
    {
      var url = BackendPlaybackBaseUrl.Resolve(new BackendClientConfig { BaseUrl = "http://api.example:9000/" });
      Assert.AreEqual("http://api.example:9000", url);
    }
  }
}
