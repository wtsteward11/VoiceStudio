using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class ClipTranscriptLinkRoundTripTests
{
  [TestMethod]
  public async Task SaveAsync_roundTrip_preserves_clipTranscriptLinks_schema_v2()
  {
    var dir = Path.Combine(Path.GetTempPath(), "vs_clip_tr_test_" + System.Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      var repo = new JsonProjectRepository(dir);
      var p = new Project
      {
        Id = "p-link",
        Name = "L",
        Tracks = new List<AudioTrack>(),
        ClipTranscriptLinks = new List<ClipTranscriptLink>
        {
          new ClipTranscriptLink
          {
            ClipId = "clip1",
            TranscriptionId = "tr1",
            AudioId = "au1",
            SegmentIds = new List<string> { "s1", "s2" },
          },
        },
      };
      await repo.SaveAsync(p);

      var loaded = await repo.GetByIdAsync("p-link");
      Assert.IsNotNull(loaded);
      Assert.AreEqual(JsonProjectRepository.CurrentPersistedProjectSchemaVersion, loaded!.PersistedProjectSchemaVersion);
      Assert.AreEqual(1, loaded.ClipTranscriptLinks.Count);
      Assert.AreEqual("clip1", loaded.ClipTranscriptLinks[0].ClipId);
      CollectionAssert.AreEqual(new[] { "s1", "s2" }, loaded.ClipTranscriptLinks[0].SegmentIds);
    }
    finally
    {
      try
      {
        foreach (var f in Directory.GetFiles(dir))
          File.Delete(f);
        Directory.Delete(dir);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"ClipTranscriptLinkRoundTripTests temp cleanup failed: {ex}");
      }
    }
  }

  [TestMethod]
  public async Task SaveAsync_roundTrip_preserves_clipTranscriptTruth_gap045()
  {
    var dir = Path.Combine(Path.GetTempPath(), "vs_truth_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      var repo = new JsonProjectRepository(dir);
      var clip = new AudioClip
      {
        Id = "c1",
        AudioId = "a1",
        Duration = TimeSpan.FromSeconds(1),
        TranscriptTruth = TranscriptTruthState.StaleAfterClipRegeneration,
      };
      var p = new Project
      {
        Id = "p-truth",
        Name = "T",
        Tracks = new List<AudioTrack>
        {
          new()
          {
            Id = "tr1",
            Name = "track",
            ProjectId = "p-truth",
            Clips = new List<AudioClip> { clip },
          },
        },
      };
      await repo.SaveAsync(p);

      var loaded = await repo.GetByIdAsync("p-truth");
      Assert.IsNotNull(loaded);
      var c = loaded!.Tracks[0].Clips[0];
      Assert.AreEqual(TranscriptTruthState.StaleAfterClipRegeneration, c.TranscriptTruth);
    }
    finally
    {
      try
      {
        foreach (var f in Directory.GetFiles(dir))
          File.Delete(f);
        Directory.Delete(dir);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"ClipTranscriptLinkRoundTripTests temp cleanup failed: {ex}");
      }
    }
  }
}
