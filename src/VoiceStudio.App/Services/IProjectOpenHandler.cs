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
}
