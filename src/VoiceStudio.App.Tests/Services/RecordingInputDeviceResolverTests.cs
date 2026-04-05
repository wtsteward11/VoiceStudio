using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class RecordingInputDeviceResolverTests
{
  [TestMethod]
  public async Task TryResolve_UnknownId_FailsClosed()
  {
    var mock = new Mock<IRecordingClient>();
    mock.Setup(c => c.GetRecordingDevicesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RecordingDevicesResponse
        {
          Devices = new[]
          {
            new RecordingDevice { Id = "dev-a", Name = "Mic A" },
          },
        });

    var r = await RecordingInputDeviceResolver.TryResolveAsync(mock.Object, null, "missing-id", CancellationToken.None)
        .ConfigureAwait(false);

    Assert.IsFalse(r.Ok);
    StringAssert.Contains(r.ErrorMessage, "Unknown");
  }

  [TestMethod]
  public void IsDefaultToken_IsCaseInsensitive()
  {
    Assert.IsTrue(RecordingInputDeviceResolver.IsDefaultToken("default"));
    Assert.IsTrue(RecordingInputDeviceResolver.IsDefaultToken("DEFAULT"));
    Assert.IsFalse(RecordingInputDeviceResolver.IsDefaultToken("dev-1"));
  }
}
