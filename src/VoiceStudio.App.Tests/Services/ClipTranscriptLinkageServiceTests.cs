using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class ClipTranscriptLinkageServiceTests
{
  private static IClipTranscriptLinkageService CreateSut() => new ClipTranscriptLinkageService();

  private static Project ProjectWithClip(string audioId, double clipDurationSec)
  {
    var clip = new AudioClip
    {
      Id = "c1",
      AudioId = audioId,
      Duration = TimeSpan.FromSeconds(clipDurationSec),
    };
    var track = new AudioTrack { Id = "t1", Clips = new List<AudioClip> { clip } };
    return new Project
    {
      Id = "p1",
      Tracks = new List<AudioTrack> { track },
      ClipTranscriptLinks = new List<ClipTranscriptLink>(),
    };
  }

  [TestMethod]
  public void UpsertLinksForTranscription_null_project_noops()
  {
    var sut = CreateSut();
    sut.UpsertLinksForTranscription(
        null,
        "tr1",
        "a1",
        new List<TranscriptionSegmentLinkInput>
        {
          new("s1", 0, 1),
        });
  }

  [TestMethod]
  public void UpsertLinksForTranscription_adds_link_with_overlapping_segments()
  {
    var sut = CreateSut();
    var p = ProjectWithClip("aud-1", 10);
    var segments = new List<TranscriptionSegmentLinkInput>
    {
      new("seg-a", 0, 2),
      new("seg-b", 9, 11),
      new("seg-c", 20, 22),
    };
    sut.UpsertLinksForTranscription(p, "tr1", "aud-1", segments);

    var link = p.ClipTranscriptLinks.Single();
    Assert.AreEqual("c1", link.ClipId);
    Assert.AreEqual("tr1", link.TranscriptionId);
    Assert.AreEqual("aud-1", link.AudioId);
    CollectionAssert.AreEquivalent(new[] { "seg-a", "seg-b" }, link.SegmentIds.ToList());
  }

  [TestMethod]
  public void RemoveLinksByClipId_removes_entry()
  {
    var sut = CreateSut();
    var p = ProjectWithClip("a", 5);
    p.ClipTranscriptLinks.Add(new ClipTranscriptLink
    {
      ClipId = "c1",
      TranscriptionId = "t",
      AudioId = "a",
      SegmentIds = new List<string> { "x" },
    });
    sut.RemoveLinksByClipId(p, "c1");
    Assert.AreEqual(0, p.ClipTranscriptLinks.Count);
  }

  [TestMethod]
  public void CopyTranscriptLinksToNewClip_duplicates_link_for_target_clip()
  {
    var sut = CreateSut();
    var p = ProjectWithClip("a", 5);
    sut.AddOrUpdateLink(p, new ClipTranscriptLink
    {
      ClipId = "c1",
      TranscriptionId = "t1",
      AudioId = "a",
      SegmentIds = new List<string> { "s1", "s2" },
    });
    sut.CopyTranscriptLinksToNewClip(p, "c1", "c2");
    Assert.AreEqual(2, p.ClipTranscriptLinks.Count);
    var forRight = p.ClipTranscriptLinks.Single(l => l.ClipId == "c2");
    Assert.AreEqual("t1", forRight.TranscriptionId);
    CollectionAssert.AreEquivalent(new[] { "s1", "s2" }, forRight.SegmentIds.ToList());
  }

  [TestMethod]
  public void ResolveSegmentIdsForClip_returns_first_link_segments()
  {
    var sut = CreateSut();
    var p = ProjectWithClip("a", 5);
    sut.AddOrUpdateLink(p, new ClipTranscriptLink
    {
      ClipId = "c1",
      TranscriptionId = "t1",
      AudioId = "a",
      SegmentIds = new List<string> { "u", "v" },
    });
    var ids = sut.ResolveSegmentIdsForClip(p, "c1");
    CollectionAssert.AreEquivalent(new[] { "u", "v" }, ids.ToList());
  }
}
