#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Controls;

namespace VoiceStudio.App.Tests.Controls;

[TestClass]
public sealed class WaveformDownsamplerTests
{
  [TestMethod]
  public void Downsample_NullSamples_ThrowsArgumentNullException()
  {
    Assert.ThrowsException<ArgumentNullException>(() =>
      WaveformDownsampler.Downsample(null!, 4, "peak"));
  }

  [TestMethod]
  public void Downsample_TargetCountZero_ReturnsEmpty()
  {
    var samples = new List<float> { 1f, 2f, 3f };
    var result = WaveformDownsampler.Downsample(samples, 0, "peak");
    Assert.AreEqual(0, result.Count);
  }

  [TestMethod]
  public void Downsample_WhenCountLessOrEqualTarget_ReturnsCopyInOrder()
  {
    var samples = new List<float> { 0.25f, -0.5f, 0.75f };
    var peak = WaveformDownsampler.Downsample(samples, 10, "peak");
    CollectionAssert.AreEqual(samples, peak);
    var rms = WaveformDownsampler.Downsample(samples, 3, "rms");
    CollectionAssert.AreEqual(samples, rms);
  }

  [TestMethod]
  public void Downsample_Peak_IsDeterministicAcrossCalls()
  {
    var samples = Enumerable.Range(0, 12).Select(i => (float)Math.Sin(i * 0.7)).ToList();
    var a = WaveformDownsampler.Downsample(samples, 4, "peak");
    var b = WaveformDownsampler.Downsample(samples, 4, "peak");
    CollectionAssert.AreEqual(a, b);
  }

  [TestMethod]
  public void Downsample_Peak_BucketUsesMaxAbsMagnitudeTimesSignOfFirstSampleInBucket()
  {
    // 6 samples → 2 buckets of 3: [0.2, -0.9, 0.1] and [0.4, 0.5, -0.3]
    var samples = new List<float> { 0.2f, -0.9f, 0.1f, 0.4f, 0.5f, -0.3f };
    var peak = WaveformDownsampler.Downsample(samples, 2, "peak");
    Assert.AreEqual(2, peak.Count);
    Assert.AreEqual(0.9f, peak[0], 1e-5f);
    Assert.AreEqual(0.5f, peak[1], 1e-5f);
  }

  [TestMethod]
  public void Downsample_Rms_AveragesPerBucket()
  {
    var samples = new List<float> { 1f, 2f, 3f, 4f, 5f, 6f };
    var rms = WaveformDownsampler.Downsample(samples, 2, "rms");
    Assert.AreEqual(2, rms.Count);
    Assert.AreEqual(2f, rms[0], 1e-5f);
    Assert.AreEqual(5f, rms[1], 1e-5f);
  }

  [TestMethod]
  public void Downsample_NonPeakMode_TreatedAsRms()
  {
    var samples = new List<float> { 2f, 4f };
    var rms = WaveformDownsampler.Downsample(samples, 1, "anythingElse");
    Assert.AreEqual(1, rms.Count);
    Assert.AreEqual(3f, rms[0], 1e-5f);
  }
}
