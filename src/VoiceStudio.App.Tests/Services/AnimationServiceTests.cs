using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public class AnimationServiceTests
{
    [TestMethod]
    public void ShouldAnimate_FalseWhenSystemAnimationsDisabled()
    {
        var sut = new AnimationService(systemAnimationsEnabled: () => false);

        Assert.IsFalse(sut.ShouldAnimate);
    }

    [TestMethod]
    public void GetAdjustedDuration_UsesSpeedMultiplier()
    {
        var sut = new AnimationService(systemAnimationsEnabled: () => true);
        sut.Settings.SpeedMultiplier = 2.0;

        var adjusted = sut.GetAdjustedDuration(TimeSpan.FromMilliseconds(400));

        Assert.AreEqual(TimeSpan.FromMilliseconds(200), adjusted);
    }

    [TestMethod]
    public void GetAdjustedDuration_ZeroWhenAnimationsDisabled()
    {
        var sut = new AnimationService(systemAnimationsEnabled: () => true);
        sut.Settings.EnableAnimations = false;

        var adjusted = sut.GetAdjustedDuration(TimeSpan.FromMilliseconds(400));

        Assert.AreEqual(TimeSpan.Zero, adjusted);
    }
}
