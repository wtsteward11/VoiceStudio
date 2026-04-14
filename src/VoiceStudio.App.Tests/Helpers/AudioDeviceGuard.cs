using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Helpers;

/// <summary>
/// Centralized guards for tests that require real WinMM audio devices.
/// Headless CI runners often have zero output/input devices.
/// </summary>
internal static class AudioDeviceGuard
{
    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern uint waveInGetNumDevs();

    public static void SkipIfNoAudioOutputDevice()
    {
        if (waveOutGetNumDevs() == 0)
        {
            Assert.Inconclusive(
                "Skipped: no audio output device available on this runner.");
        }
    }

    public static void SkipIfNoAudioInputDevice()
    {
        if (waveInGetNumDevs() == 0)
        {
            Assert.Inconclusive(
                "Skipped: no audio input device (microphone) available on this runner.");
        }
    }
}
