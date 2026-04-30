using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Core.Models;

namespace VoiceStudio.App.Tests.Core.Models;

[TestClass]
public sealed class DialogueApiModelsJsonTests
{
  [TestMethod]
  public void RegenerateDialogueSegmentRequest_OmitsNullTrackId()
  {
    var req = new RegenerateDialogueSegmentRequest
    {
      TranscriptId = "tr1",
      ProfileId = "p1",
      TrackId = null,
      ReplaceExistingClip = true,
      EditedText = "hello",
    };
    var json = JsonSerializer.Serialize(req);
    Assert.IsFalse(json.Contains("trackId", StringComparison.OrdinalIgnoreCase));
    Assert.IsFalse(json.Contains("track_id", StringComparison.OrdinalIgnoreCase));
    StringAssert.Contains(json, "tr1");
    StringAssert.Contains(json, "replace_existing_clip");
  }
}
