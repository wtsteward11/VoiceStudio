using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Effects Mixer state API. PR-17: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class MixerStateClient : IMixerStateClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with pipeline from CreateMixerStateClient.
    /// </summary>
    internal MixerStateClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<MixerState> GetMixerStateAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<MixerState>($"/api/mixer/state/{Uri.EscapeDataString(projectId)}", cancellationToken).ConfigureAwait(false);
      return result ?? throw new BackendDeserializationException("Failed to deserialize mixer state");
    }

    /// <inheritdoc />
    public Task<MixerState> UpdateMixerStateAsync(string projectId, MixerState state, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<MixerState, MixerState>($"/api/mixer/state/{Uri.EscapeDataString(projectId)}", state, cancellationToken);

    /// <inheritdoc />
    public async Task<MixerState> ResetMixerStateAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.SendRequestAsync<object, MixerState>(
          $"/api/mixer/state/{Uri.EscapeDataString(projectId)}/reset", null, HttpMethod.Post, cancellationToken).ConfigureAwait(false);
      return result ?? throw new BackendDeserializationException("Failed to deserialize mixer state");
    }

    /// <inheritdoc />
    public async Task<List<MixerPreset>> GetMixerPresetsAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<List<MixerPreset>>($"/api/mixer/presets/{Uri.EscapeDataString(projectId)}", cancellationToken).ConfigureAwait(false);
      return result ?? throw new BackendDeserializationException("Failed to deserialize mixer presets");
    }

    /// <inheritdoc />
    public async Task<MixerPreset> GetMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<MixerPreset>(
          $"/api/mixer/presets/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(presetId)}", cancellationToken).ConfigureAwait(false);
      return result ?? throw new BackendDeserializationException("Failed to deserialize mixer preset");
    }

    /// <inheritdoc />
    public Task<MixerPreset> CreateMixerPresetAsync(string projectId, MixerPreset preset, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<MixerPreset, MixerPreset>($"/api/mixer/presets/{Uri.EscapeDataString(projectId)}", preset, cancellationToken);

    /// <inheritdoc />
    public Task<MixerPreset> UpdateMixerPresetAsync(string projectId, string presetId, MixerPreset preset, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<MixerPreset, MixerPreset>(
          $"/api/mixer/presets/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(presetId)}", preset, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/mixer/presets/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(presetId)}", null, HttpMethod.Delete, cancellationToken).ConfigureAwait(false);
      return true;
    }

    /// <inheritdoc />
    public async Task<MixerState> ApplyMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.SendRequestAsync<object, MixerState>(
          $"/api/mixer/presets/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(presetId)}/apply", null, HttpMethod.Post, cancellationToken).ConfigureAwait(false);
      return result ?? throw new BackendDeserializationException("Failed to deserialize mixer state");
    }

    /// <inheritdoc />
    public Task<MixerMaster> UpdateMixerMasterAsync(string projectId, MixerMaster master, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<MixerMaster, MixerMaster>($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/master", master, cancellationToken);

    /// <inheritdoc />
    public Task<MixerSend> CreateMixerSendAsync(string projectId, MixerSend send, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<MixerSend, MixerSend>($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/sends", send, cancellationToken);

    /// <inheritdoc />
    public Task<MixerSend> UpdateMixerSendAsync(string projectId, string sendId, MixerSend send, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<MixerSend, MixerSend>(
          $"/api/mixer/state/{Uri.EscapeDataString(projectId)}/sends/{Uri.EscapeDataString(sendId)}", send, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteMixerSendAsync(string projectId, string sendId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/mixer/state/{Uri.EscapeDataString(projectId)}/sends/{Uri.EscapeDataString(sendId)}", null, HttpMethod.Delete, cancellationToken).ConfigureAwait(false);
      return true;
    }

    /// <inheritdoc />
    public Task<MixerReturn> CreateMixerReturnAsync(string projectId, MixerReturn returnBus, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<MixerReturn, MixerReturn>($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/returns", returnBus, cancellationToken);

    /// <inheritdoc />
    public Task<MixerReturn> UpdateMixerReturnAsync(string projectId, string returnId, MixerReturn returnBus, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<MixerReturn, MixerReturn>(
          $"/api/mixer/state/{Uri.EscapeDataString(projectId)}/returns/{Uri.EscapeDataString(returnId)}", returnBus, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteMixerReturnAsync(string projectId, string returnId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/mixer/state/{Uri.EscapeDataString(projectId)}/returns/{Uri.EscapeDataString(returnId)}", null, HttpMethod.Delete, cancellationToken).ConfigureAwait(false);
      return true;
    }

    /// <inheritdoc />
    public Task<MixerSubGroup> CreateMixerSubGroupAsync(string projectId, MixerSubGroup subgroup, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<MixerSubGroup, MixerSubGroup>($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/subgroups", subgroup, cancellationToken);

    /// <inheritdoc />
    public Task<MixerSubGroup> UpdateMixerSubGroupAsync(string projectId, string subgroupId, MixerSubGroup subgroup, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<MixerSubGroup, MixerSubGroup>(
          $"/api/mixer/state/{Uri.EscapeDataString(projectId)}/subgroups/{Uri.EscapeDataString(subgroupId)}", subgroup, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteMixerSubGroupAsync(string projectId, string subgroupId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/mixer/state/{Uri.EscapeDataString(projectId)}/subgroups/{Uri.EscapeDataString(subgroupId)}", null, HttpMethod.Delete, cancellationToken).ConfigureAwait(false);
      return true;
    }
  }
}
