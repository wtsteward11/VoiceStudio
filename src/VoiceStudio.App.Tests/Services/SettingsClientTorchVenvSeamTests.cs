using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>GAP-062: SettingsClient routes torch venv diagnostics through IBackendClient without exposing on IBackendClient.</summary>
  [TestClass]
  public class SettingsClientTorchVenvSeamTests
  {
    [TestMethod]
    public async Task GetTorchVenvStatusAsync_CallsCorrectEndpoint()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend
          .Setup(b => b.SendRequestAsync<object, TorchVenvStatusResponse>(
              "/api/settings/torch-venv/effective",
              null,
              HttpMethod.Get,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TorchVenvStatusResponse { Source = "torch_venv_resolver" });

      var sut = new SettingsClient(mockBackend.Object);
      _ = await sut.GetTorchVenvStatusAsync().ConfigureAwait(false);

      mockBackend.Verify(
          b => b.SendRequestAsync<object, TorchVenvStatusResponse>(
              "/api/settings/torch-venv/effective",
              null,
              HttpMethod.Get,
              It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task GetTorchVenvStatusAsync_DeserializesResponse()
    {
      var mockBackend = new Mock<IBackendClient>();
      var body = new TorchVenvStatusResponse
      {
        Source = "torch_venv_resolver",
        Families =
        {
          new TorchVenvFamilyStatus
          {
            Family = "venv_core_tts",
            Status = "missing",
            Engines = { "xtts_v2" },
          },
        },
      };
      mockBackend
          .Setup(b => b.SendRequestAsync<object, TorchVenvStatusResponse>(
              It.IsAny<string>(),
              null,
              HttpMethod.Get,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(body);

      var sut = new SettingsClient(mockBackend.Object);
      var result = await sut.GetTorchVenvStatusAsync().ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.AreEqual("torch_venv_resolver", result.Source);
      Assert.AreEqual(1, result.Families.Count);
      Assert.AreEqual("venv_core_tts", result.Families[0].Family);
      Assert.AreEqual("missing", result.Families[0].Status);
    }

    [TestMethod]
    public async Task GetTorchVenvStatusAsync_ReturnsNullWhenBackendReturnsNull()
    {
      var mockBackend = new Mock<IBackendClient>();
      mockBackend
          .Setup(b => b.SendRequestAsync<object, TorchVenvStatusResponse>(
              It.IsAny<string>(),
              null,
              HttpMethod.Get,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync((TorchVenvStatusResponse?)null);

      var sut = new SettingsClient(mockBackend.Object);
      var result = await sut.GetTorchVenvStatusAsync().ConfigureAwait(false);

      Assert.IsNull(result);
    }
  }
}
