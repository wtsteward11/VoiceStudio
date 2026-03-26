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
  /// Client for macro and automation API. PR-9: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class MacroClient : IMacroClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with mock pipeline.
    /// </summary>
    internal MacroClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<List<Macro>> GetMacrosAsync(string? projectId = null, CancellationToken cancellationToken = default)
    {
      var url = "/api/macros";
      if (!string.IsNullOrEmpty(projectId))
        url += $"?project_id={Uri.EscapeDataString(projectId)}";
      var result = await _pipeline.GetAsync<List<Macro>>(url, cancellationToken);
      return result ?? new List<Macro>();
    }

    /// <inheritdoc />
    public async Task<Macro> GetMacroAsync(string macroId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<Macro>($"/api/macros/{Uri.EscapeDataString(macroId)}", cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize macro");
    }

    /// <inheritdoc />
    public Task<Macro> CreateMacroAsync(Macro macro, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<Macro, Macro>("/api/macros", macro, cancellationToken);

    /// <inheritdoc />
    public Task<Macro> UpdateMacroAsync(string macroId, Macro macro, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<Macro, Macro>($"/api/macros/{Uri.EscapeDataString(macroId)}", macro, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteMacroAsync(string macroId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/macros/{Uri.EscapeDataString(macroId)}", null, HttpMethod.Delete, cancellationToken);
      return true;
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteMacroAsync(string macroId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/macros/{Uri.EscapeDataString(macroId)}/execute", null, HttpMethod.Post, cancellationToken);
      return true;
    }

    /// <inheritdoc />
    public async Task<MacroExecutionStatus> GetMacroExecutionStatusAsync(string macroId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<MacroExecutionStatus>(
          $"/api/macros/{Uri.EscapeDataString(macroId)}/execution-status", cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize macro execution status");
    }

    /// <inheritdoc />
    public async Task<List<AutomationCurve>> GetAutomationCurvesAsync(string trackId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<List<AutomationCurve>>(
          $"/api/macros/automation/{Uri.EscapeDataString(trackId)}", cancellationToken);
      return result ?? new List<AutomationCurve>();
    }

    /// <inheritdoc />
    public Task<AutomationCurve> CreateAutomationCurveAsync(AutomationCurve curve, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<AutomationCurve, AutomationCurve>("/api/macros/automation", curve, cancellationToken);

    /// <inheritdoc />
    public Task<AutomationCurve> UpdateAutomationCurveAsync(string curveId, AutomationCurve curve, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<AutomationCurve, AutomationCurve>(
          $"/api/macros/automation/{Uri.EscapeDataString(curveId)}", curve, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteAutomationCurveAsync(string curveId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/macros/automation/{Uri.EscapeDataString(curveId)}", null, HttpMethod.Delete, cancellationToken);
      return true;
    }
  }
}
