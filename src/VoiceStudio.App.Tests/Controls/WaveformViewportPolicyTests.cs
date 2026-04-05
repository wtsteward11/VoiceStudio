#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Controls;

namespace VoiceStudio.App.Tests.Controls;

[TestClass]
public sealed class WaveformViewportPolicyTests
{
  [TestMethod]
  public void ComputeNormalizedViewport_DurationInvalid_ReturnsFullWindow()
  {
    var (s, w) = WaveformViewportPolicy.ComputeNormalizedViewport(5, 0, 2);
    Assert.AreEqual(0, s);
    Assert.AreEqual(1, w);

    (s, w) = WaveformViewportPolicy.ComputeNormalizedViewport(5, -1, 2);
    Assert.AreEqual(0, s);
    Assert.AreEqual(1, w);
  }

  [TestMethod]
  public void ComputeNormalizedViewport_Zoom2_CentersOnFocus_Clamped()
  {
    var (s, w) = WaveformViewportPolicy.ComputeNormalizedViewport(5, 10, 2);
    Assert.AreEqual(0.25, s, 1e-9);
    Assert.AreEqual(0.5, w, 1e-9);
  }

  [TestMethod]
  public void ComputeNormalizedViewport_Zoom2_FocusNearStart_ClampsStartAtZero()
  {
    var (s, w) = WaveformViewportPolicy.ComputeNormalizedViewport(1, 10, 2);
    Assert.AreEqual(0, s, 1e-9);
    Assert.AreEqual(0.5, w, 1e-9);
  }

  [TestMethod]
  public void ComputeNormalizedViewport_Zoom2_FocusNearEnd_ClampsEndAtOne()
  {
    var (s, w) = WaveformViewportPolicy.ComputeNormalizedViewport(9.5, 10, 2);
    Assert.AreEqual(0.5, s, 1e-9);
    Assert.AreEqual(0.5, w, 1e-9);
  }

  [TestMethod]
  public void ComputeNormalizedViewport_IsDeterministic()
  {
    var a = WaveformViewportPolicy.ComputeNormalizedViewport(3.3, 11.7, 1.8);
    var b = WaveformViewportPolicy.ComputeNormalizedViewport(3.3, 11.7, 1.8);
    Assert.AreEqual(a.StartNormalized, b.StartNormalized, 1e-12);
    Assert.AreEqual(a.WidthNormalized, b.WidthNormalized, 1e-12);
  }

  [TestMethod]
  public void SliceSamples_Null_Throws()
  {
    Assert.ThrowsException<ArgumentNullException>(() =>
      WaveformViewportPolicy.SliceSamples(null!, 0, 1));
  }

  [TestMethod]
  public void SliceSamples_Empty_ReturnsEmpty()
  {
    var r = WaveformViewportPolicy.SliceSamples(new List<float>(), 0, 1);
    Assert.AreEqual(0, r.Count);
  }

  [TestMethod]
  public void SliceSamples_QuarterToThreeQuarter_ReturnsMiddleHalf()
  {
    var samples = Enumerable.Range(0, 100).Select(i => (float)i).ToList();
    var slice = WaveformViewportPolicy.SliceSamples(samples, 0.25, 0.5);
    Assert.AreEqual(50, slice.Count);
    Assert.AreEqual(25f, slice[0]);
    Assert.AreEqual(74f, slice[49]);
  }

  [TestMethod]
  public void ComputePlaybackNormalizedInViewport_InsideWindow_ReturnsLocal()
  {
    var v = WaveformViewportPolicy.ComputePlaybackNormalizedInViewport(5, 10, 0.25, 0.5);
    Assert.AreEqual(0.5, v, 1e-9);
  }

  [TestMethod]
  public void ComputePlaybackNormalizedInViewport_OutsideWindow_ReturnsNegativeOne()
  {
    var v = WaveformViewportPolicy.ComputePlaybackNormalizedInViewport(1, 10, 0.25, 0.5);
    Assert.AreEqual(-1, v);
  }

  [TestMethod]
  public void ComputePlaybackNormalizedInViewport_NoDuration_ReturnsNegativeOne()
  {
    var v = WaveformViewportPolicy.ComputePlaybackNormalizedInViewport(1, 0, 0, 1);
    Assert.AreEqual(-1, v);
  }

  [TestMethod]
  public void IsFullViewport_FullAndPartial()
  {
    Assert.IsTrue(WaveformViewportPolicy.IsFullViewport(0, 1));
    Assert.IsFalse(WaveformViewportPolicy.IsFullViewport(0.1, 0.9));
  }
}
