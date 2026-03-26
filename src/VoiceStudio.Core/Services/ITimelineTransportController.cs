using System.Threading.Tasks;

namespace VoiceStudio.Core.Services;

/// <summary>
/// Transport control surface for the Timeline panel.
/// Decouples orchestration from UI-tree lookup (PanelHost, TimelineView).
/// </summary>
public interface ITimelineTransportController
{
    bool IsPlaying { get; }

    Task PlayAsync();

    void Pause();

    void Stop();
}
