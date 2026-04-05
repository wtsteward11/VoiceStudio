using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <inheritdoc />
public sealed class TimelineSelectedProjectGate : ITimelineSelectedProjectGate
{
  private Project? _selectedProject;
  private readonly object _sync = new();

  public Project? SelectedProject
  {
    get
    {
      lock (_sync)
        return _selectedProject;
    }
  }

  public void SetSelectedProject(Project? project)
  {
    lock (_sync)
      _selectedProject = project;
  }
}
