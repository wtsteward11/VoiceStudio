using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Deserialization contract for POST /api/emotion/apply-extended (GAP-050).
  /// </summary>
  [TestClass]
  public class EmotionApplyExtendedResponseContractTests
  {
    [TestMethod]
    public void Deserialize_SnakeCase_MapsProsodyHandlingAndMappingSource()
    {
      const string json = """
        {
          "audio_id": "emotion_out_1",
          "audio_url": "/api/voice/audio/emotion_out_1",
          "emotion_mapping_source": "canonical_preset",
          "prosody_handling": {
            "action": "applied",
            "applied_operations": ["pitch_shift", "time_stretch"],
            "skipped_operations": [],
            "warnings": [],
            "errors": [],
            "pitch_factor": 1.12,
            "rate_factor": 1.1,
            "volume_factor": 1.06,
            "context": "emotion_apply_extended"
          }
        }
        """;

      var model = JsonSerializer.Deserialize<EmotionApplyExtendedResponse>(
        json,
        JsonSerializerOptionsFactory.BackendApi);

      Assert.IsNotNull(model);
      Assert.AreEqual("emotion_out_1", model!.AudioId);
      Assert.AreEqual("/api/voice/audio/emotion_out_1", model.AudioUrl);
      Assert.AreEqual("canonical_preset", model.EmotionMappingSource);
      Assert.IsNotNull(model.ProsodyHandling);
      Assert.AreEqual("applied", model.ProsodyHandling!.Action);
      Assert.AreEqual(2, model.ProsodyHandling.AppliedOperations.Count);
      Assert.AreEqual("emotion_apply_extended", model.ProsodyHandling.Context);
    }
  }
}
