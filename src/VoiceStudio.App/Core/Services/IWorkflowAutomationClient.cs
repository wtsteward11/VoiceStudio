using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for workflow automation API (Create, Update, Execute).
  /// Use instead of IBackendClient for WorkflowAutomation panel.
  /// </summary>
  public interface IWorkflowAutomationClient
  {
    Task<List<Workflow>> GetWorkflowsAsync(int skip = 0, int limit = 100, bool enabledOnly = false, CancellationToken cancellationToken = default);

    Task<Workflow> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);

    Task<Workflow> CreateWorkflowAsync(WorkflowCreateRequest request, CancellationToken cancellationToken = default);

    Task<Workflow> UpdateWorkflowAsync(string workflowId, WorkflowUpdateRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);

    Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? inputData = null, CancellationToken cancellationToken = default);
  }
}
