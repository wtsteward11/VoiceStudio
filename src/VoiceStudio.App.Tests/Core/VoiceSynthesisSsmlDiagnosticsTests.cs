using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Core
{
  [TestClass]
  public class VoiceSynthesisSsmlDiagnosticsTests
  {
    [TestMethod]
    public void DeserializeVoiceSynthesisResponse_WithSsmlHandling_RoundTrips()
    {
      var json = """
        {
          "audio_id": "a1",
          "audio_url": "/api/voice/audio/a1",
          "duration": 1.5,
          "quality_score": 0.9,
          "quality_metrics": null,
          "ssml_handling": {
            "ssml_detected": true,
            "capability_class": "plain_text_only",
            "action": "stripped_warned",
            "warnings": ["Engine is plain-text-only"],
            "engine_id": "tacotron2"
          }
        }
        """;

      var o = JsonSerializer.Deserialize<VoiceSynthesisResponse>(json, JsonSerializerOptionsFactory.BackendApi);
      Assert.IsNotNull(o);
      Assert.IsNotNull(o.SsmlHandling);
      Assert.IsTrue(o.SsmlHandling.SsmlDetected);
      Assert.AreEqual("stripped_warned", o.SsmlHandling.Action);
      Assert.AreEqual("tacotron2", o.SsmlHandling.EngineId);
      Assert.AreEqual(1, o.SsmlHandling.Warnings.Count);
    }
  }
}
