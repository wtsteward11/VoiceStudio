using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services
{
  [TestClass]
  public class RateLimitToastDedupeTests
  {
    private static readonly DateTime BaseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void ShouldSuppress_FirstCall_ReturnsFalse()
    {
      var cache = new Dictionary<string, DateTime>();
      var lockObj = new object();
      var result = RateLimitToastDedupe.ShouldSuppress(
          cache, "Too many requests", "/api/quality/history", 10, BaseTime, lockObj);
      Assert.IsFalse(result, "First call should not suppress");
      Assert.AreEqual(1, cache.Count);
    }

    [TestMethod]
    public void ShouldSuppress_DuplicateWithinWindow_ReturnsTrue()
    {
      var cache = new Dictionary<string, DateTime>();
      var lockObj = new object();
      RateLimitToastDedupe.ShouldSuppress(
          cache, "Too many requests", "/api/quality/history", 10, BaseTime, lockObj);
      var result = RateLimitToastDedupe.ShouldSuppress(
          cache, "Too many requests", "/api/quality/history", 10, BaseTime.AddSeconds(1), lockObj);
      Assert.IsTrue(result, "Duplicate within 10 seconds should suppress");
    }

    [TestMethod]
    public void ShouldSuppress_AfterWindow_ReturnsFalse()
    {
      var cache = new Dictionary<string, DateTime>();
      var lockObj = new object();
      RateLimitToastDedupe.ShouldSuppress(
          cache, "Too many requests", "/api/quality/history", 10, BaseTime, lockObj);
      var result = RateLimitToastDedupe.ShouldSuppress(
          cache, "Too many requests", "/api/quality/history", 10, BaseTime.AddSeconds(11), lockObj);
      Assert.IsFalse(result, "After 10 seconds should not suppress");
    }

    [TestMethod]
    public void ShouldSuppress_DifferentEndpoints_DoNotDedupe()
    {
      var cache = new Dictionary<string, DateTime>();
      var lockObj = new object();
      RateLimitToastDedupe.ShouldSuppress(
          cache, "Too many requests", "/api/quality/history", 10, BaseTime, lockObj);
      var result = RateLimitToastDedupe.ShouldSuppress(
          cache, "Too many requests", "/api/profiles", 10, BaseTime.AddSeconds(1), lockObj);
      Assert.IsFalse(result, "Different endpoints should not dedupe");
    }
  }
}
