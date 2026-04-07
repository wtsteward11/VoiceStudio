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

    [TestMethod]
    public void DeserializeVoiceSynthesisResponse_WithSsmlAndProsody_RoundTrips()
    {
      var json = """
        {
          "audio_id": "a1",
          "audio_url": "/api/audio/a1",
          "duration": 1.5,
          "quality_score": 0.9,
          "ssml_handling": {
            "ssml_detected": true,
            "capability_class": "plain_text_only",
            "action": "stripped_warned",
            "warnings": ["strip"],
            "engine_id": "tacotron2"
          },
          "prosody_handling": {
            "action": "applied",
            "warnings": ["prosody"],
            "skipped_operations": [],
            "applied_operations": [],
            "errors": [],
            "pitch_factor": 1.0,
            "rate_factor": 1.0,
            "volume_factor": 1.0,
            "context": ""
          },
          "emotion_mapping_source": "canonical_preset",
          "emotion_preset_apply_failure_message": null
        }
        """;

      var o = JsonSerializer.Deserialize<VoiceSynthesisResponse>(json, JsonSerializerOptionsFactory.BackendApi);
      Assert.IsNotNull(o);
      Assert.IsNotNull(o.SsmlHandling);
      Assert.IsNotNull(o.ProsodyHandling);
      Assert.AreEqual("canonical_preset", o.EmotionMappingSource);
      Assert.AreEqual("stripped_warned", o.SsmlHandling.Action);
      Assert.AreEqual("applied", o.ProsodyHandling.Action);
    }

    [TestMethod]
    public void BuildSynthesisCapabilityCombinedNotice_MergesSsmlProsodyAndPresetFailure()
    {
      var ssml = new SsmlHandlingDiagnostics
      {
        Action = "stripped_warned",
        Warnings = new List<string> { "w1" },
      };
      var prosody = new ProsodyHandlingDiagnosticsDto
      {
        Warnings = new List<string> { "p1" },
      };
      var n = ActionableErrorTranslator.BuildSynthesisCapabilityCombinedNotice(ssml, prosody, "fallback msg");
      Assert.IsNotNull(n);
      StringAssert.Contains(n.PrimaryMessage, "SSML");
      StringAssert.Contains(n.PrimaryMessage, "fallback msg");
    }
  }
}
