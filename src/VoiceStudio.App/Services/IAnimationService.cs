using System;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Helpers;

namespace VoiceStudio.App.Services;

public sealed class AnimationSettings
{
    public bool EnableAnimations { get; set; } = true;
    public double SpeedMultiplier { get; set; } = 1.0;
}

public interface IAnimationService
{
    AnimationSettings Settings { get; }
    bool ShouldAnimate { get; }
    TimeSpan GetAdjustedDuration(TimeSpan baseDuration);

    void FadeIn(UIElement element, TimeSpan? duration = null);
    void FadeOut(UIElement element, TimeSpan? duration = null);
    void SlideIn(UIElement element, SlideDirection direction, TimeSpan? duration = null);
    void SlideOut(UIElement element, SlideDirection direction, TimeSpan? duration = null);
    void StartLoadingSpinner(UIElement element, TimeSpan? cycleDuration = null);
    void StopLoadingSpinner(UIElement element);
}
