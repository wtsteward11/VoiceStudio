using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Handles saving project-related state (e.g. mixer state). Implemented by mixer/timeline adapter.
/// </summary>
public interface IProjectSaveHandler
{
    Task SaveMixerStateIfNeededAsync(CancellationToken ct = default);
}
