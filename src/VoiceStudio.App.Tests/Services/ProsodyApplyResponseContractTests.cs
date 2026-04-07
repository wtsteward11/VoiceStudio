using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Deserialization contract for POST /api/prosody/apply (GAP-023).
  /// </summary>
  [TestClass]
  public class ProsodyApplyResponseContractTests
  {
    [TestMethod]
    public void Deserialize_SnakeCase_MapsProsodyHandling()
    {
      const string json = """
        {
          "audio_id": "a1",
          "original_audio_id": "a0",
          "audio_url": "/api/voice/audio/a1",
          "duration": 1.5,
          "prosody_applied": false,
          "config_applied": { "pitch": 1.0, "rate": 1.0, "volume": 1.0, "intonation": null },
          "prosody_handling": {
            "action": "none",
            "applied_operations": [],
            "skipped_operations": [{"operation": "all", "reason": "identity_request"}],
            "warnings": [],
            "errors": [],
            "pitch_factor": 1.0,
            "rate_factor": 1.0,
            "volume_factor": 1.0,
            "context": "prosody_apply"
          }
        }
        """;

      var model = JsonSerializer.Deserialize<ProsodyViewModel.ProsodyApplyResponse>(
          json,
          JsonSerializerOptionsFactory.BackendApi);

      Assert.IsNotNull(model);
      Assert.AreEqual("a1", model!.AudioId);
      Assert.AreEqual("/api/voice/audio/a1", model.AudioUrl);
      Assert.IsFalse(model.ProsodyApplied);
      Assert.IsNotNull(model.ProsodyHandling);
      Assert.AreEqual("none", model.ProsodyHandling!.Action);
      Assert.AreEqual(1, model.ProsodyHandling.SkippedOperations.Count);
    }
  }
}
