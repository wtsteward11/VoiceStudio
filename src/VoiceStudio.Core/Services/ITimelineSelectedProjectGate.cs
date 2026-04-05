using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services;

/// <summary>
/// Holds the timeline panel's current <see cref="Project"/> for cross-panel resolution (GAP-045).
/// Updated only from the Timeline panel ViewModel when the selected project changes.
/// </summary>
public interface ITimelineSelectedProjectGate
{
  Project? SelectedProject { get; }

  void SetSelectedProject(Project? project);
}
