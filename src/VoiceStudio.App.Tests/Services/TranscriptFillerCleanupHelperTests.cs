using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptFillerCleanupHelperTests
{
  [TestMethod]
  public void RemoveFillers_Null_ReturnsEmpty()
  {
    var r = TranscriptFillerCleanupHelper.RemoveFillers(null);
    Assert.AreEqual(string.Empty, r.CleanedText);
    Assert.AreEqual(0, r.RemovedOccurrenceCount);
  }

  [TestMethod]
  public void RemoveFillers_SingleToken_Punctuation()
  {
    var r = TranscriptFillerCleanupHelper.RemoveFillers("Hello, Um. world");
    Assert.AreEqual("Hello, world", r.CleanedText);
    Assert.AreEqual(1, r.RemovedOccurrenceCount);
    Assert.IsTrue(r.TermsSummary.Contains("um×", StringComparison.Ordinal));
  }

  [TestMethod]
  public void RemoveFillers_Phrase_YouKnow()
  {
    var r = TranscriptFillerCleanupHelper.RemoveFillers("Well you know the thing");
    Assert.AreEqual("Well the thing", r.CleanedText);
    Assert.AreEqual(1, r.RemovedOccurrenceCount);
    StringAssert.Contains(r.TermsSummary, "you know");
  }

  [TestMethod]
  public void RemoveFillers_PhraseBeforeSingletonLike()
  {
    var r = TranscriptFillerCleanupHelper.RemoveFillers("you know like really");
    Assert.AreEqual("really", r.CleanedText);
    Assert.AreEqual(2, r.RemovedOccurrenceCount);
  }

  [TestMethod]
  public void RemoveFillers_Preserves_NonFillerLikeness()
  {
    var r = TranscriptFillerCleanupHelper.RemoveFillers("It is likely fine");
    Assert.AreEqual("It is likely fine", r.CleanedText);
    Assert.AreEqual(0, r.RemovedOccurrenceCount);
  }

  [TestMethod]
  public void RemoveFillers_Normalizes_InternalWhitespaceToSingleSpace()
  {
    var r = TranscriptFillerCleanupHelper.RemoveFillers("a   um   b");
    Assert.AreEqual("a b", r.CleanedText);
    Assert.AreEqual(1, r.RemovedOccurrenceCount);
  }

  [TestMethod]
  public void RemoveFillers_EnabledSubset_OnlyUm()
  {
    var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "um" };
    var r = TranscriptFillerCleanupHelper.RemoveFillers("um and uh", phrases, tokens);
    Assert.AreEqual("and uh", r.CleanedText);
    Assert.AreEqual(1, r.RemovedOccurrenceCount);
  }

  [TestMethod]
  public void GetRemovalPlan_Subset_CountsOnlyEnabled()
  {
    var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "um" };
    var plan = TranscriptFillerCleanupHelper.GetRemovalPlan("um uh", phrases, tokens);
    Assert.AreEqual(1, plan.Count);
    Assert.AreEqual("um", plan[0].CatalogKey, StringComparer.OrdinalIgnoreCase);
  }

  [TestMethod]
  public void GetPreviewAfterRemoval_LikeDisabled_KeepsLike()
  {
    var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "um" };
    var preview = TranscriptFillerCleanupHelper.GetPreviewAfterRemoval("I like um", phrases, tokens);
    Assert.AreEqual("I like", preview.TrimEnd());
  }

  [TestMethod]
  public void GetPreviewAfterRemoval_LikeEnabled_RemovesLike()
  {
    var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "um", "like" };
    var preview = TranscriptFillerCleanupHelper.GetPreviewAfterRemoval("I like um", phrases, tokens);
    Assert.AreEqual("I", preview.Trim());
  }

  [TestMethod]
  public void TermsSummary_IsCommaSeparatedWithCounts()
  {
    var r = TranscriptFillerCleanupHelper.RemoveFillers("um and uh");
    StringAssert.Contains(r.TermsSummary, "um×");
    StringAssert.Contains(r.TermsSummary, "uh×");
  }

  [TestMethod]
  public void RiskySingleTokenKeys_ContainsLike()
  {
    Assert.IsTrue(TranscriptFillerCleanupHelper.RiskySingleTokenKeys.Contains("like"));
  }
}
