using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for macro and automation API. Use instead of IBackendClient for macro panel.
  /// </summary>
  public interface IMacroClient
  {
    Task<List<Macro>> GetMacrosAsync(string? projectId = null, CancellationToken cancellationToken = default);
    Task<Macro> GetMacroAsync(string macroId, CancellationToken cancellationToken = default);
    Task<Macro> CreateMacroAsync(Macro macro, CancellationToken cancellationToken = default);
    Task<Macro> UpdateMacroAsync(string macroId, Macro macro, CancellationToken cancellationToken = default);
    Task<bool> DeleteMacroAsync(string macroId, CancellationToken cancellationToken = default);
    Task<bool> ExecuteMacroAsync(string macroId, CancellationToken cancellationToken = default);
    Task<MacroExecutionStatus> GetMacroExecutionStatusAsync(string macroId, CancellationToken cancellationToken = default);
    Task<List<AutomationCurve>> GetAutomationCurvesAsync(string trackId, CancellationToken cancellationToken = default);
    Task<AutomationCurve> CreateAutomationCurveAsync(AutomationCurve curve, CancellationToken cancellationToken = default);
    Task<AutomationCurve> UpdateAutomationCurveAsync(string curveId, AutomationCurve curve, CancellationToken cancellationToken = default);
    Task<bool> DeleteAutomationCurveAsync(string curveId, CancellationToken cancellationToken = default);
  }
}
