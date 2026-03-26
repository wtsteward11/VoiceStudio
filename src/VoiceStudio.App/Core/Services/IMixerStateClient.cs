using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Effects Mixer state API (channels, sends, returns, sub-groups, master, presets).
  /// PR-17: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public interface IMixerStateClient
  {
    Task<MixerState> GetMixerStateAsync(string projectId, CancellationToken cancellationToken = default);
    Task<MixerState> UpdateMixerStateAsync(string projectId, MixerState state, CancellationToken cancellationToken = default);
    Task<MixerState> ResetMixerStateAsync(string projectId, CancellationToken cancellationToken = default);
    Task<List<MixerPreset>> GetMixerPresetsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<MixerPreset> GetMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default);
    Task<MixerPreset> CreateMixerPresetAsync(string projectId, MixerPreset preset, CancellationToken cancellationToken = default);
    Task<MixerPreset> UpdateMixerPresetAsync(string projectId, string presetId, MixerPreset preset, CancellationToken cancellationToken = default);
    Task<bool> DeleteMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default);
    Task<MixerState> ApplyMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default);
    Task<MixerMaster> UpdateMixerMasterAsync(string projectId, MixerMaster master, CancellationToken cancellationToken = default);
    Task<MixerSend> CreateMixerSendAsync(string projectId, MixerSend send, CancellationToken cancellationToken = default);
    Task<MixerSend> UpdateMixerSendAsync(string projectId, string sendId, MixerSend send, CancellationToken cancellationToken = default);
    Task<bool> DeleteMixerSendAsync(string projectId, string sendId, CancellationToken cancellationToken = default);
    Task<MixerReturn> CreateMixerReturnAsync(string projectId, MixerReturn returnBus, CancellationToken cancellationToken = default);
    Task<MixerReturn> UpdateMixerReturnAsync(string projectId, string returnId, MixerReturn returnBus, CancellationToken cancellationToken = default);
    Task<bool> DeleteMixerReturnAsync(string projectId, string returnId, CancellationToken cancellationToken = default);
    Task<MixerSubGroup> CreateMixerSubGroupAsync(string projectId, MixerSubGroup subgroup, CancellationToken cancellationToken = default);
    Task<MixerSubGroup> UpdateMixerSubGroupAsync(string projectId, string subgroupId, MixerSubGroup subgroup, CancellationToken cancellationToken = default);
    Task<bool> DeleteMixerSubGroupAsync(string projectId, string subgroupId, CancellationToken cancellationToken = default);
  }
}
