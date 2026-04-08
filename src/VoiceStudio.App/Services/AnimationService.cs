using System;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Helpers;
using Windows.UI.ViewManagement;

namespace VoiceStudio.App.Services;

/// <summary>
/// Canonical animation facade that respects reduced-motion preferences.
/// </summary>
public sealed class AnimationService : IAnimationService
{
    private readonly Func<bool> _systemAnimationsEnabled;

    public AnimationService(Func<bool>? systemAnimationsEnabled = null)
    {
        _systemAnimationsEnabled = systemAnimationsEnabled ?? (() =>
        {
            try
            {
                return new UISettings().AnimationsEnabled;
            }
            catch
            {
                return true;
            }
        });
    }

    public AnimationSettings Settings { get; } = new();
    public bool ShouldAnimate => Settings.EnableAnimations && _systemAnimationsEnabled();

    public TimeSpan GetAdjustedDuration(TimeSpan baseDuration)
    {
        if (!ShouldAnimate)
            return TimeSpan.Zero;

        var speed = Settings.SpeedMultiplier <= 0 ? 1.0 : Settings.SpeedMultiplier;
        return TimeSpan.FromMilliseconds(baseDuration.TotalMilliseconds / speed);
    }

    public void FadeIn(UIElement element, TimeSpan? duration = null)
    {
        if (!ShouldAnimate)
            return;
        AnimationHelper.FadeIn(element, GetAdjustedDuration(duration ?? TimeSpan.FromMilliseconds(300)));
    }

    public void FadeOut(UIElement element, TimeSpan? duration = null)
    {
        if (!ShouldAnimate)
            return;
        AnimationHelper.FadeOut(element, GetAdjustedDuration(duration ?? TimeSpan.FromMilliseconds(300)));
    }

    public void SlideIn(UIElement element, SlideDirection direction, TimeSpan? duration = null)
    {
        if (!ShouldAnimate)
            return;
        AnimationHelper.SlideIn(element, direction, GetAdjustedDuration(duration ?? TimeSpan.FromMilliseconds(300)));
    }

    public void SlideOut(UIElement element, SlideDirection direction, TimeSpan? duration = null)
    {
        if (!ShouldAnimate)
            return;
        AnimationHelper.SlideOut(element, direction, GetAdjustedDuration(duration ?? TimeSpan.FromMilliseconds(300)));
    }

    public void StartLoadingSpinner(UIElement element, TimeSpan? cycleDuration = null)
    {
        if (!ShouldAnimate)
            return;
        AnimationHelper.StartSpinning(element, GetAdjustedDuration(cycleDuration ?? TimeSpan.FromSeconds(1)));
    }

    public void StopLoadingSpinner(UIElement element)
    {
        AnimationHelper.StopSpinning(element);
    }
}
