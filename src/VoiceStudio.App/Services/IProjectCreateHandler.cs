using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Handles creating a new project. Implemented by timeline panel adapter.
/// </summary>
public interface IProjectCreateHandler
{
    Task CreateNewAsync(CancellationToken ct = default);
}
