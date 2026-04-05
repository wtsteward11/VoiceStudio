using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Authoritative shell save: mixer, backend project metadata, and local JSON snapshot (lane GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01).
/// </summary>
public interface IProjectSaveHandler
{
    Task SaveProjectAsync(CancellationToken cancellationToken = default);
}
