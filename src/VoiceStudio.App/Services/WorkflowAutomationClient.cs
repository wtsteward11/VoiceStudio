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
  /// Client for workflow automation API. PR-10: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class WorkflowAutomationClient : IWorkflowAutomationClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with mock pipeline.
    /// </summary>
    internal WorkflowAutomationClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<List<Workflow>> GetWorkflowsAsync(int skip = 0, int limit = 100, bool enabledOnly = false, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string> { $"skip={skip}", $"limit={limit}" };
      if (enabledOnly)
        queryParams.Add("enabled_only=true");
      var url = $"/api/workflows?{string.Join("&", queryParams)}";
      var result = await _pipeline.GetAsync<List<Workflow>>(url, cancellationToken);
      return result ?? new List<Workflow>();
    }

    /// <inheritdoc />
    public async Task<Workflow> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<Workflow>($"/api/workflows/{Uri.EscapeDataString(workflowId)}", cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize workflow");
    }

    /// <inheritdoc />
    public Task<Workflow> CreateWorkflowAsync(WorkflowCreateRequest request, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<WorkflowCreateRequest, Workflow>("/api/workflows", request, cancellationToken);

    /// <inheritdoc />
    public Task<Workflow> UpdateWorkflowAsync(string workflowId, WorkflowUpdateRequest request, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<WorkflowUpdateRequest, Workflow>($"/api/workflows/{Uri.EscapeDataString(workflowId)}", request, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/workflows/{Uri.EscapeDataString(workflowId)}", null, HttpMethod.Delete, cancellationToken);
      return true;
    }

    /// <inheritdoc />
    public async Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? inputData = null, CancellationToken cancellationToken = default)
    {
      var request = new Dictionary<string, object?>
      {
        ["workflow_id"] = workflowId,
        ["input_data"] = inputData
      };
      var result = await _pipeline.PostAsync<Dictionary<string, object?>, WorkflowExecutionResult>(
          $"/api/workflows/{Uri.EscapeDataString(workflowId)}/execute", request, cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize workflow execution result");
    }
  }
}
