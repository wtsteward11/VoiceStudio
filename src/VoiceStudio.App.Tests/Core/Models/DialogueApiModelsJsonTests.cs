using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Tests.Core.Models;

[TestClass]
public sealed class DialogueApiModelsJsonTests
{
  private static readonly JsonSerializerOptions Options = JsonSerializerOptionsFactory.BackendApi;

  [TestMethod]
  public void RegenerateDialogueSegmentRequest_SerializesTranscriptIdAsSnakeCase()
  {
    var req = new RegenerateDialogueSegmentRequest
    {
      TranscriptId = "tr1",
      ProfileId = "p1",
      ReplaceExistingClip = false,
    };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"transcript_id\":\"tr1\"");
  }

  [TestMethod]
  public void RegenerateDialogueSegmentRequest_SerializesProfileIdAsSnakeCase()
  {
    var req = new RegenerateDialogueSegmentRequest
    {
      TranscriptId = "tr1",
      ProfileId = "prof-x",
      ReplaceExistingClip = false,
    };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"profile_id\":\"prof-x\"");
  }

  [TestMethod]
  public void RegenerateDialogueSegmentRequest_IncludesTrackIdWhenPresent()
  {
    var req = new RegenerateDialogueSegmentRequest
    {
      TranscriptId = "tr1",
      ProfileId = "p1",
      TrackId = "track-99",
      ReplaceExistingClip = false,
    };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"track_id\":\"track-99\"");
  }

  [TestMethod]
  public void RegenerateDialogueSegmentRequest_OmitsTrackIdWhenNull()
  {
    var req = new RegenerateDialogueSegmentRequest
    {
      TranscriptId = "tr1",
      ProfileId = "p1",
      TrackId = null,
      ReplaceExistingClip = true,
      EditedText = "hello",
    };
    var json = JsonSerializer.Serialize(req, Options);
    Assert.IsFalse(json.Contains("\"track_id\"", StringComparison.Ordinal));
    StringAssert.Contains(json, "\"transcript_id\":\"tr1\"");
    StringAssert.Contains(json, "\"replace_existing_clip\":true");
    StringAssert.Contains(json, "\"edited_text\":\"hello\"");
  }

  [TestMethod]
  public void RegenerateDialogueSegmentRequest_SerializesProjectIdAndSessionIdWhenPresent()
  {
    var req = new RegenerateDialogueSegmentRequest
    {
      TranscriptId = "tr1",
      ProfileId = "p1",
      ProjectId = "proj-a",
      SessionId = "sess-b",
      ReplaceExistingClip = false,
    };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"project_id\":\"proj-a\"");
    StringAssert.Contains(json, "\"session_id\":\"sess-b\"");
  }

  [TestMethod]
  public void RegenerateDialogueSegmentRequest_ReplaceExistingClipUsesSnakeCase()
  {
    var req = new RegenerateDialogueSegmentRequest
    {
      TranscriptId = "tr1",
      ProfileId = "p1",
      ReplaceExistingClip = true,
    };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"replace_existing_clip\":true");
    Assert.IsFalse(json.Contains("ReplaceExistingClip", StringComparison.Ordinal));
  }

  [TestMethod]
  public void RegenerateDialogueSegmentRequest_EditedTextUsesSnakeCase()
  {
    var req = new RegenerateDialogueSegmentRequest
    {
      TranscriptId = "tr1",
      ProfileId = "p1",
      ReplaceExistingClip = false,
      EditedText = "edited line",
    };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"edited_text\":\"edited line\"");
  }

  [TestMethod]
  public void RegenerateDialogueSegmentResponse_DeserializesTopLevelIdsFromSnakeCase()
  {
    const string json = """
      {
        "project_id": "p99",
        "session_id": "s1",
        "transcript_id": "tr1",
        "segment_id": "seg1",
        "status": "ok",
        "audio_id": "aud1",
        "generated_audio_id": "gen1",
        "library_asset_id": "lib1",
        "timeline_clip_id": "tl1",
        "routed_engine": "piper",
        "duration": 1.5,
        "segment": { "transcript_id": "tr1", "id": "seg1", "timeline_clip_id": "tl1" }
      }
      """;
    var r = JsonSerializer.Deserialize<RegenerateDialogueSegmentResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.AreEqual("p99", r.ProjectId);
    Assert.AreEqual("s1", r.SessionId);
    Assert.AreEqual("tr1", r.TranscriptId);
    Assert.AreEqual("seg1", r.SegmentId);
    Assert.AreEqual("aud1", r.AudioId);
    Assert.AreEqual("gen1", r.GeneratedAudioId);
    Assert.AreEqual("lib1", r.LibraryAssetId);
    Assert.AreEqual("tl1", r.TimelineClipId);
    Assert.AreEqual("piper", r.RoutedEngine);
    Assert.AreEqual(1.5, r.Duration, 0.001);
  }

  [TestMethod]
  public void RegenerateDialogueSegmentResponse_SegmentDeserializesTimelineClipId()
  {
    const string json = """
      {
        "project_id": "p",
        "session_id": "",
        "transcript_id": "t",
        "segment_id": "s",
        "status": "x",
        "audio_id": "a",
        "library_asset_id": "l",
        "timeline_clip_id": "top-tl",
        "routed_engine": "e",
        "duration": 0,
        "segment": { "transcript_id": "t", "id": "s", "timeline_clip_id": "nested-tl" }
      }
      """;
    var r = JsonSerializer.Deserialize<RegenerateDialogueSegmentResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.AreEqual("nested-tl", r.Segment.TimelineClipId);
  }

  [TestMethod]
  public void CreateTimelineClipsFromTranscriptRequest_SerializesTrackIdAsSnakeCase()
  {
    var req = new CreateTimelineClipsFromTranscriptRequest
    {
      TrackId = "track-1",
      ReplaceExisting = true,
    };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"track_id\":\"track-1\"");
    StringAssert.Contains(json, "\"replace_existing\":true");
  }

  [TestMethod]
  public void CreateTimelineClipsFromTranscriptResponse_DeserializesCreatedClipIdsFromSnakeCase()
  {
    const string json = """
      {
        "transcript_id": "t1",
        "session_id": "s1",
        "track_id": "k1",
        "created_clip_ids": ["a", "b"],
        "segment_count": 2,
        "status": "done"
      }
      """;
    var r = JsonSerializer.Deserialize<CreateTimelineClipsFromTranscriptResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.AreEqual("t1", r.TranscriptId);
    Assert.AreEqual(2, r.CreatedClipIds.Count);
    Assert.AreEqual("a", r.CreatedClipIds[0]);
    Assert.AreEqual("b", r.CreatedClipIds[1]);
    Assert.AreEqual(2, r.SegmentCount);
    Assert.AreEqual("done", r.Status);
  }
}