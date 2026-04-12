using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Handles opening projects. Implemented by timeline panel adapter.
/// </summary>
public interface IProjectOpenHandler
{
    Task OpenProjectPickerAsync(CancellationToken ct = default);
    Task OpenProjectByIdAsync(string projectId, string projectName, CancellationToken ct = default);

    /// <summary>
    /// GAP-067 slice 4: open a project from a shell-associated file path (.voiceproj JSON on disk).
    /// </summary>
    Task OpenProjectByPathAsync(string filePath, CancellationToken ct = default);
}
