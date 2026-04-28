using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Core.Models;

namespace VoiceStudio.App.Tests.Models;

[TestClass]
public sealed class VoiceProfileEngineCompatibilityTagsTests
{
  [TestMethod]
  public void TryParseAllowedEngines_FirstExactPrefix_ReturnsTrueAndAllowList()
  {
    var tags = new List<string> { "favorite", "vs:engines:piper,xtts" };

    var ok = VoiceProfileEngineCompatibilityTags.TryParseAllowedEngines(tags, out var allow);

    Assert.IsTrue(ok);
    Assert.IsNotNull(allow);
    Assert.IsTrue(allow!.Contains("piper"));
    Assert.IsTrue(allow.Contains("xtts"));
  }

  [TestMethod]
  public void TryParseAllowedEngines_WrongCasePrefix_NotMatched_ReturnsFalse()
  {
    var tags = new List<string> { "VS:ENGINES:piper" };

    var ok = VoiceProfileEngineCompatibilityTags.TryParseAllowedEngines(tags, out var allow);

    Assert.IsFalse(ok);
    Assert.IsNull(allow);
  }

  [TestMethod]
  public void TryParseAllowedEngines_EmptyPayload_ReturnsFalse()
  {
    var tags = new List<string> { "vs:engines:" };

    var ok = VoiceProfileEngineCompatibilityTags.TryParseAllowedEngines(tags, out var allow);

    Assert.IsFalse(ok);
    Assert.IsNull(allow);
  }

  [TestMethod]
  public void ParseAllowedEngineIds_NormalizesOrderAndCaseDupes()
  {
    var tags = new List<string> { "vs:engines:Zeta,alpha,ZETA" };

    var ids = VoiceProfileEngineCompatibilityTags.ParseAllowedEngineIds(tags);

    CollectionAssert.AreEqual(new[] { "alpha", "Zeta" }, ids.ToArray(), StringComparer.Ordinal);
  }

  [TestMethod]
  public void ReplaceEnginesTag_StripsAllVsEnginesPrefixes_PreservesOtherTags()
  {
    var current = new List<string>
    {
      "vs:engines:old",
      "favorite",
      "vs:engines:stale,ids",
      "custom:foo",
    };

    var merged = VoiceProfileEngineCompatibilityTags.ReplaceEnginesTag(current, new[] { "piper" });

    Assert.AreEqual(3, merged.Count);
    StringAssert.StartsWith(merged[0], "vs:engines:");
    Assert.IsTrue(merged[0].Contains("piper"));
    CollectionAssert.Contains(merged, "favorite");
    CollectionAssert.Contains(merged, "custom:foo");
    Assert.AreEqual(1, merged.Count(t => t.StartsWith("vs:engines:", StringComparison.Ordinal)));
  }

  [TestMethod]
  public void ReplaceEnginesTag_EmptyEngineIds_RemovesAllVsEnginesLeavesRest()
  {
    var current = new List<string> { "vs:engines:piper", "favorite" };

    var merged = VoiceProfileEngineCompatibilityTags.ReplaceEnginesTag(current, Array.Empty<string>());

    CollectionAssert.AreEquivalent(new[] { "favorite" }, merged.ToArray());
  }

  [TestMethod]
  public void ReplaceEnginesTag_WritesSingleNormalizedTag_WhenMultipleIds()
  {
    var merged = VoiceProfileEngineCompatibilityTags.ReplaceEnginesTag(
        new List<string>(),
        new[] { "zeta", "alpha", "ALPHA" });

    Assert.AreEqual(1, merged.Count);
    Assert.AreEqual("vs:engines:alpha,zeta", merged[0]);
  }

  [TestMethod]
  public void TryParseAllowedEngines_MalformedEmptySegments_IgnoresWhitespaceOnlyParts()
  {
    var tags = new List<string> { "vs:engines:piper,  ,xtts" };

    var ok = VoiceProfileEngineCompatibilityTags.TryParseAllowedEngines(tags, out var allow);

    Assert.IsTrue(ok);
    Assert.IsNotNull(allow);
    Assert.AreEqual(2, allow!.Count);
  }
}
