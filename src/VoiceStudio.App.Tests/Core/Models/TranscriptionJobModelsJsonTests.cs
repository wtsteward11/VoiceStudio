using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Core.Models;

[TestClass]
public sealed class TranscriptionJobModelsJsonTests
{
  private static readonly JsonSerializerOptions Options = JsonSerializerOptionsFactory.BackendApi;

  [TestMethod]
  public void Request_SerializesAudioId()
  {
    var req = new TranscriptionJobRequest { AudioId = "aud-1" };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"audio_id\":\"aud-1\"");
  }

  [TestMethod]
  public void Request_SerializesWordTimestamps()
  {
    var req = new TranscriptionJobRequest { AudioId = "a", WordTimestamps = true };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"word_timestamps\":true");
  }

  [TestMethod]
  public void Request_SerializesSimulate()
  {
    var req = new TranscriptionJobRequest { AudioId = "a", Simulate = true };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"simulate\":true");
  }

  [TestMethod]
  public void Response_DeserializesJobId()
  {
    const string json = """{"job_id":"job-99","audio_id":"a1","status":"completed","mode":"real","is_simulated":false,"real_transcription_performed":true}""";
    var r = JsonSerializer.Deserialize<TranscriptionJobResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.AreEqual("job-99", r.JobId);
  }

  [TestMethod]
  public void Response_DeserializesTranscriptId()
  {
    const string json = """{"job_id":"j1","audio_id":"a1","transcript_id":"tid-7","status":"completed","mode":"real","is_simulated":false,"real_transcription_performed":true}""";
    var r = JsonSerializer.Deserialize<TranscriptionJobResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.AreEqual("tid-7", r.TranscriptId);
  }

  [TestMethod]
  public void Response_DeserializesIsSimulated()
  {
    const string json = """{"job_id":"j1","audio_id":"a1","status":"completed","mode":"simulation","is_simulated":true,"real_transcription_performed":false}""";
    var r = JsonSerializer.Deserialize<TranscriptionJobResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.IsTrue(r.IsSimulated);
  }

  [TestMethod]
  public void Response_DeserializesRealTranscriptionPerformed()
  {
    const string json = """{"job_id":"j1","audio_id":"a1","status":"completed","mode":"real","is_simulated":false,"real_transcription_performed":true}""";
    var r = JsonSerializer.Deserialize<TranscriptionJobResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.IsTrue(r.RealTranscriptionPerformed);
  }

  [TestMethod]
  public void Response_DeserializesNestedSegmentIds()
  {
    const string json = """
{"job_id":"j1","audio_id":"a1","status":"completed","mode":"real","is_simulated":false,"real_transcription_performed":true,"transcript":{"id":"tr1","text":"hi","duration":1.0,"segments":[{"id":"seg-0","start":0,"end":0.5,"text":"hi"}]}}
""";
    var r = JsonSerializer.Deserialize<TranscriptionJobResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.IsNotNull(r.Transcript);
    Assert.IsTrue(r.Transcript.Segments.Count > 0);
    Assert.AreEqual("seg-0", r.Transcript.Segments[0].Id);
  }

  [TestMethod]
  public void Response_UnavailableDeserializesBlocker()
  {
    const string json = """{"job_id":"j1","audio_id":"a1","status":"unavailable","mode":"unavailable","is_simulated":false,"real_transcription_performed":false,"blocker":"engine down"}""";
    var r = JsonSerializer.Deserialize<TranscriptionJobResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.AreEqual("unavailable", r.Status);
    Assert.AreEqual("engine down", r.Blocker);
  }

  [TestMethod]
  public void Request_SerializesAsyncMode()
  {
    var req = new TranscriptionJobRequest { AudioId = "a", AsyncMode = true };
    var json = JsonSerializer.Serialize(req, Options);
    StringAssert.Contains(json, "\"async_mode\":true");
  }

  [TestMethod]
  public void Response_DeserializesProgress()
  {
    const string json = """{"job_id":"j1","audio_id":"a1","status":"running","mode":"real","is_simulated":false,"real_transcription_performed":false,"progress":0.25}""";
    var r = JsonSerializer.Deserialize<TranscriptionJobResponse>(json, Options);
    Assert.IsNotNull(r);
    Assert.AreEqual(0.25f, r.Progress);
  }

}



