using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class RecordingInputCommandStateTests
{
  [TestMethod]
  public void SetSelected_TrimsAndClearsEmpty()
  {
    var sut = new RecordingInputCommandState();
    sut.SetSelectedInputSourceId("  abc  ");
    Assert.AreEqual("abc", sut.SelectedInputSourceId);
    sut.SetSelectedInputSourceId(" ");
    Assert.IsNull(sut.SelectedInputSourceId);
  }
}
