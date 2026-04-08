using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Deserialization contract for POST /api/emotion/preview (same model as apply-extended, GAP-050).
  /// </summary>
  [TestClass]
  public class EmotionPreviewResponseContractTests
  {
    [TestMethod]
    public void Deserialize_SnakeCase_MapsSameShapeAsApplyExtended()
    {
      const string json = """
        {
          "audio_id": "emotion_preview_abc12345",
          "audio_url": "/api/voice/audio/emotion_preview_abc12345",
          "emotion_mapping_source": "canonical_preset",
          "prosody_handling": {
            "action": "none",
            "applied_operations": [],
            "skipped_operations": [],
            "warnings": [],
            "errors": [],
            "pitch_factor": 1.0,
            "rate_factor": 1.0,
            "volume_factor": 1.0,
            "context": "emotion_preview"
          }
        }
        """;

      var model = JsonSerializer.Deserialize<EmotionApplyExtendedResponse>(
        json,
        JsonSerializerOptionsFactory.BackendApi);

      Assert.IsNotNull(model);
      Assert.AreEqual("emotion_preview_abc12345", model!.AudioId);
      Assert.AreEqual("/api/voice/audio/emotion_preview_abc12345", model.AudioUrl);
      Assert.AreEqual("canonical_preset", model.EmotionMappingSource);
      Assert.IsNotNull(model.ProsodyHandling);
      Assert.AreEqual("emotion_preview", model.ProsodyHandling!.Context);
    }
  }
}
