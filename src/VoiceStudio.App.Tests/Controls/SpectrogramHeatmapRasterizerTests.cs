#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Controls;

namespace VoiceStudio.App.Tests.Controls;

[TestClass]
public sealed class SpectrogramHeatmapRasterizerTests
{
  [TestMethod]
  public void TryRasterize_NullOrEmptyFrames_ReturnsFalse()
  {
    Assert.IsFalse(SpectrogramHeatmapRasterizer.TryRasterize(
      null!,
      1.0,
      1024,
      512,
      64,
      64,
      out _,
      out _,
      out _,
      out _));

    Assert.IsFalse(SpectrogramHeatmapRasterizer.TryRasterize(
      Array.Empty<SpectrogramHeatmapRasterizer.SpectrogramRasterFrame>(),
      1.0,
      1024,
      512,
      64,
      64,
      out _,
      out _,
      out _,
      out _));
  }

  [TestMethod]
  public void TryRasterize_IsDeterministicAcrossCalls()
  {
    var frames = BuildRampFrames(frameCount: 80, binCount: 80);
    var okA = SpectrogramHeatmapRasterizer.TryRasterize(
      frames,
      1.0,
      SpectrogramHeatmapRasterizer.DefaultMaxRenderWidth,
      SpectrogramHeatmapRasterizer.DefaultMaxRenderHeight,
      SpectrogramHeatmapRasterizer.DefaultMinRenderWidth,
      SpectrogramHeatmapRasterizer.DefaultMinRenderHeight,
      out var wA,
      out var hA,
      out var pixA,
      out var durA);

    var okB = SpectrogramHeatmapRasterizer.TryRasterize(
      frames,
      1.0,
      SpectrogramHeatmapRasterizer.DefaultMaxRenderWidth,
      SpectrogramHeatmapRasterizer.DefaultMaxRenderHeight,
      SpectrogramHeatmapRasterizer.DefaultMinRenderWidth,
      SpectrogramHeatmapRasterizer.DefaultMinRenderHeight,
      out var wB,
      out var hB,
      out var pixB,
      out var durB);

    Assert.IsTrue(okA);
    Assert.IsTrue(okB);
    Assert.AreEqual(wA, wB);
    Assert.AreEqual(hA, hB);
    Assert.AreEqual(durA, durB, 1e-9);
    CollectionAssert.AreEqual(pixA, pixB);
  }

  [TestMethod]
  public void TryRasterize_NonFiniteZoom_UsesSafeDefault()
  {
    var frames = BuildRampFrames(frameCount: 64, binCount: 64);
    var okNan = SpectrogramHeatmapRasterizer.TryRasterize(
      frames,
      double.NaN,
      1024,
      512,
      64,
      64,
      out var wNan,
      out var hNan,
      out var pixNan,
      out _);

    var okOne = SpectrogramHeatmapRasterizer.TryRasterize(
      frames,
      1.0,
      1024,
      512,
      64,
      64,
      out var wOne,
      out var hOne,
      out var pixOne,
      out _);

    Assert.IsTrue(okNan);
    Assert.IsTrue(okOne);
    Assert.AreEqual(wNan, wOne);
    Assert.AreEqual(hNan, hOne);
    CollectionAssert.AreEqual(pixNan, pixOne);
  }

  [TestMethod]
  public void GetHeatmapColor_Endpoints_AreOpaque()
  {
    var z = SpectrogramHeatmapRasterizer.GetHeatmapColor(0f);
    var o = SpectrogramHeatmapRasterizer.GetHeatmapColor(1f);
    Assert.AreEqual((byte)255, z.A);
    Assert.AreEqual((byte)255, o.A);
  }

  private static List<SpectrogramHeatmapRasterizer.SpectrogramRasterFrame> BuildRampFrames(int frameCount, int binCount)
  {
    var frames = new List<SpectrogramHeatmapRasterizer.SpectrogramRasterFrame>();
    for (var i = 0; i < frameCount; i++)
    {
      var mags = Enumerable.Range(0, binCount).Select(j => (float)(j + i) / (frameCount + binCount)).ToList();
      frames.Add(new SpectrogramHeatmapRasterizer.SpectrogramRasterFrame(i * 0.01, mags));
    }

    return frames;
  }
}
