using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class RecordingDeviceAvailabilityServiceTests
{
  [TestMethod]
  public async Task Refresh_FiresInputDevicesChanged_OnBackendChange()
  {
    var seq = 0;
    var mock = new Mock<IRecordingClient>();
    mock.Setup(c => c.GetRecordingDevicesAsync(It.IsAny<CancellationToken>()))
        .Returns(() =>
        {
          seq++;
          if (seq == 1)
          {
            return Task.FromResult<RecordingDevicesResponse?>(new RecordingDevicesResponse
            {
              Devices = new[] { new RecordingDevice { Id = "a", Name = "A" } },
            });
          }

          return Task.FromResult<RecordingDevicesResponse?>(new RecordingDevicesResponse
          {
            Devices = new[]
            {
              new RecordingDevice { Id = "a", Name = "A" },
              new RecordingDevice { Id = "b", Name = "B" },
            },
          });
        });

    var sut = new RecordingDeviceAvailabilityService(mock.Object);
    var fires = 0;
    sut.InputDevicesChanged += (_, _) => fires++;

    await sut.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
    await sut.RefreshAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsTrue(fires >= 1);
    var snap = sut.GetSnapshot();
    Assert.AreEqual(2, snap.Count);
    Assert.IsTrue(snap.Any(d => d.Id == "b"));
  }

  [TestMethod]
  public void IsBackendDeviceIdListed_UsesLastSnapshot()
  {
    var mock = new Mock<IRecordingClient>();
    mock.Setup(c => c.GetRecordingDevicesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RecordingDevicesResponse
        {
          Devices = new[] { new RecordingDevice { Id = "x", Name = "X" } },
        });
    var sut = new RecordingDeviceAvailabilityService(mock.Object);
    sut.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
    Assert.IsTrue(sut.IsBackendDeviceIdListed("x"));
    Assert.IsFalse(sut.IsBackendDeviceIdListed("y"));
  }
}
