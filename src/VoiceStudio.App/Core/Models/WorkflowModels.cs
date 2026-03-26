using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Variable in a workflow.
  /// </summary>
  public class WorkflowVariable
  {
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
  }

  /// <summary>
  /// Step in a workflow.
  /// </summary>
  public class WorkflowStep
  {
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "synthesize", "effect", "export", "control"
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
    public int Order { get; set; }  // Execution order
  }

  /// <summary>
  /// Complete workflow definition.
  /// </summary>
  public class Workflow
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<WorkflowStep> Steps { get; set; } = new();
    public List<WorkflowVariable> Variables { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

  /// <summary>
  /// Request to create a workflow.
  /// </summary>
  public class WorkflowCreateRequest
  {
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<WorkflowStep>? Steps { get; set; }
    public List<WorkflowVariable>? Variables { get; set; }
    public bool IsEnabled { get; set; } = true;
  }

  /// <summary>
  /// Request to update a workflow.
  /// </summary>
  public class WorkflowUpdateRequest
  {
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<WorkflowStep>? Steps { get; set; }
    public List<WorkflowVariable>? Variables { get; set; }
    public bool? IsEnabled { get; set; }
  }

  /// <summary>
  /// Request for workflow execution (backend expects workflow_id, input_data).
  /// </summary>
  public class WorkflowExecuteRequest
  {
    [System.Text.Json.Serialization.JsonPropertyName("workflow_id")]
    public string WorkflowId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("input_data")]
    public Dictionary<string, object>? InputData { get; set; }
  }

  /// <summary>
  /// Result of workflow execution.
  /// </summary>
  public class WorkflowExecutionResult
  {
    public string WorkflowId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "completed", "failed", "cancelled"
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string? CurrentStepName { get; set; }
    public double Progress { get; set; }  // 0.0 to 1.0
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Outputs { get; set; } = new(); // Output data from workflow
    public string StartedAt { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
  }
}
