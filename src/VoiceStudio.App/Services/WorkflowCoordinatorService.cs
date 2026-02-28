// Panel Workflow Integration
// WorkflowCoordinatorService - Orchestrates multi-panel workflow sequences

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Workflow definition for multi-panel sequences orchestrated by the coordinator.
  /// </summary>
  public class CoordinatedWorkflowDefinition
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<CoordinatedWorkflowStep> Steps { get; set; } = new();
  }

  /// <summary>
  /// A single step in a coordinated workflow sequence.
  /// Named to avoid ambiguity with VoiceStudio.Core.Models.WorkflowStep.
  /// </summary>
  public class CoordinatedWorkflowStep
  {
    public int Order { get; set; }
    public string PanelId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool IsOptional { get; set; }
  }

  /// <summary>
  /// Workflow execution context.
  /// </summary>
  public class WorkflowContext
  {
    public string WorkflowId { get; set; } = string.Empty;
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public int CurrentStep { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Pending;
    public Dictionary<string, object> Data { get; set; } = new();
    public string? ErrorMessage { get; set; }
  }

  /// <summary>
  /// Workflow execution status.
  /// </summary>
  public enum WorkflowStatus
  {
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
  }

  /// <summary>
  /// Interface for WorkflowCoordinatorService.
  /// </summary>
  public interface IWorkflowCoordinatorService
  {
    /// <summary>
    /// Starts a "Clone from Library" workflow.
    /// </summary>
    Task<WorkflowContext> StartCloneFromLibraryAsync(string assetId, string assetPath, string? assetName = null, bool useQuickClone = true);

    /// <summary>
    /// Starts a "Synthesize with Voice" workflow.
    /// </summary>
    Task<WorkflowContext> StartSynthesizeWithVoiceAsync(string profileId, string? profileName = null);

    /// <summary>
    /// Starts a "Play from Library" workflow.
    /// </summary>
    Task<WorkflowContext> StartPlayFromLibraryAsync(string assetId, string assetPath, string? assetName = null);

    /// <summary>
    /// Gets the current workflow execution context.
    /// </summary>
    WorkflowContext? CurrentWorkflow { get; }

    /// <summary>
    /// Cancels the current workflow.
    /// </summary>
    void CancelCurrentWorkflow();

    /// <summary>
    /// Event raised when a workflow starts.
    /// </summary>
    event EventHandler<WorkflowContext>? WorkflowStarted;

    /// <summary>
    /// Event raised when a workflow completes.
    /// </summary>
    event EventHandler<WorkflowContext>? WorkflowCompleted;

    /// <summary>
    /// Event raised when a workflow fails.
    /// </summary>
    event EventHandler<WorkflowContext>? WorkflowFailed;
  }

  /// <summary>
  /// Service that coordinates multi-panel workflow sequences.
  /// Provides high-level APIs for common workflows like "Clone from Library",
  /// "Synthesize with Voice", etc.
  /// </summary>
  public class WorkflowCoordinatorService : IWorkflowCoordinatorService, IDisposable
  {
    private readonly IEventAggregator? _eventAggregator;
    private WorkflowContext? _currentWorkflow;
    private bool _disposed;

    public WorkflowContext? CurrentWorkflow => _currentWorkflow;

    public event EventHandler<WorkflowContext>? WorkflowStarted;
    public event EventHandler<WorkflowContext>? WorkflowCompleted;
    public event EventHandler<WorkflowContext>? WorkflowFailed;

    public WorkflowCoordinatorService()
    {
      _eventAggregator = AppServices.TryGetEventAggregator();
    }

    /// <summary>
    /// Starts a "Clone from Library" workflow.
    /// This workflow:
    /// 1. Navigates to Quick Clone or Voice Cloning Wizard panel
    /// 2. Publishes CloneReferenceSelectedEvent with the asset
    /// 3. The target panel receives the event and loads the audio
    /// </summary>
    /// <param name="assetId">The unique identifier of the audio asset</param>
    /// <param name="assetPath">The file path to the audio asset</param>
    /// <param name="assetName">Optional display name for the asset</param>
    /// <param name="useQuickClone">True to use Quick Clone panel, false for full wizard</param>
    public Task<WorkflowContext> StartCloneFromLibraryAsync(
      string assetId,
      string assetPath,
      string? assetName = null,
      bool useQuickClone = true)
    {
      var context = CreateWorkflowContext("clone-from-library");

      try
      {
        // Store workflow data
        context.Data["assetId"] = assetId;
        context.Data["assetPath"] = assetPath;
        context.Data["assetName"] = assetName ?? string.Empty;
        context.Data["useQuickClone"] = useQuickClone;

        StartWorkflow(context);

        // Step 1: Request panel navigation (if panel navigation service available)
        var targetPanelId = useQuickClone ? "voice-quick-clone" : "voice-cloning-wizard";
        context.CurrentStep = 1;
        context.Data["targetPanelId"] = targetPanelId;

        // Publish navigation request event
        _eventAggregator?.Publish(new PanelNavigationRequestEvent(
          "workflow-coordinator",
          targetPanelId));

        // Step 2: Publish the clone reference event
        context.CurrentStep = 2;
        _eventAggregator?.Publish(new CloneReferenceSelectedEvent(
          "library-panel",
          assetId,
          assetPath,
          assetName));

        // Mark workflow as completed
        CompleteWorkflow(context);
        return Task.FromResult(context);
      }
      catch (Exception ex)
      {
        FailWorkflow(context, ex.Message);
        return Task.FromResult(context);
      }
    }

    /// <summary>
    /// Starts a "Synthesize with Voice" workflow.
    /// This workflow:
    /// 1. Navigates to the Synthesis panel
    /// 2. Publishes VoiceProfileSelectedEvent with the profile
    /// 3. The Synthesis panel receives the event and selects the voice
    /// </summary>
    /// <param name="profileId">The unique identifier of the voice profile</param>
    /// <param name="profileName">Optional display name for the profile</param>
    public Task<WorkflowContext> StartSynthesizeWithVoiceAsync(
      string profileId,
      string? profileName = null)
    {
      var context = CreateWorkflowContext("synthesize-with-voice");

      try
      {
        // Store workflow data
        context.Data["profileId"] = profileId;
        context.Data["profileName"] = profileName ?? string.Empty;

        StartWorkflow(context);

        // Step 1: Request panel navigation to synthesis
        context.CurrentStep = 1;
        _eventAggregator?.Publish(new PanelNavigationRequestEvent(
          "workflow-coordinator",
          "synthesis-panel"));

        // Step 2: Publish the voice profile selection event
        context.CurrentStep = 2;
        _eventAggregator?.Publish(new VoiceProfileSelectedEvent(
          "library-panel",
          profileId,
          profileName));

        // Mark workflow as completed
        CompleteWorkflow(context);
        return Task.FromResult(context);
      }
      catch (Exception ex)
      {
        FailWorkflow(context, ex.Message);
        return Task.FromResult(context);
      }
    }

    /// <summary>
    /// Starts a "Play from Library" workflow.
    /// This workflow publishes a PlaybackRequestedEvent for the audio asset.
    /// </summary>
    /// <param name="assetId">The unique identifier of the audio asset</param>
    /// <param name="assetPath">The file path to the audio asset</param>
    /// <param name="assetName">Optional display name for the asset</param>
    public Task<WorkflowContext> StartPlayFromLibraryAsync(
      string assetId,
      string assetPath,
      string? assetName = null)
    {
      var context = CreateWorkflowContext("play-from-library");

      try
      {
        // Store workflow data
        context.Data["assetId"] = assetId;
        context.Data["assetPath"] = assetPath;
        context.Data["assetName"] = assetName ?? string.Empty;

        StartWorkflow(context);

        // Step 1: Publish playback request
        context.CurrentStep = 1;
        _eventAggregator?.Publish(new PlaybackRequestedEvent(
          "library-panel",
          assetId,
          assetPath,
          assetName));

        // Mark workflow as completed
        CompleteWorkflow(context);
        return Task.FromResult(context);
      }
      catch (Exception ex)
      {
        FailWorkflow(context, ex.Message);
        return Task.FromResult(context);
      }
    }

    /// <summary>
    /// Cancels the current workflow.
    /// </summary>
    public void CancelCurrentWorkflow()
    {
      if (_currentWorkflow != null && _currentWorkflow.Status == WorkflowStatus.Running)
      {
        _currentWorkflow.Status = WorkflowStatus.Cancelled;
        System.Diagnostics.Debug.WriteLine(
          $"[WorkflowCoordinator] Workflow cancelled: {_currentWorkflow.WorkflowId}");
      }
    }

    #region Private Helpers

    private WorkflowContext CreateWorkflowContext(string workflowId)
    {
      return new WorkflowContext
      {
        WorkflowId = workflowId,
        ExecutionId = Guid.NewGuid().ToString(),
        StartedAt = DateTime.UtcNow,
        Status = WorkflowStatus.Pending
      };
    }

    private void StartWorkflow(WorkflowContext context)
    {
      _currentWorkflow = context;
      context.Status = WorkflowStatus.Running;
      WorkflowStarted?.Invoke(this, context);
      System.Diagnostics.Debug.WriteLine(
        $"[WorkflowCoordinator] Workflow started: {context.WorkflowId} ({context.ExecutionId})");
    }

    private void CompleteWorkflow(WorkflowContext context)
    {
      context.Status = WorkflowStatus.Completed;
      WorkflowCompleted?.Invoke(this, context);
      System.Diagnostics.Debug.WriteLine(
        $"[WorkflowCoordinator] Workflow completed: {context.WorkflowId} ({context.ExecutionId})");
    }

    private void FailWorkflow(WorkflowContext context, string errorMessage)
    {
      context.Status = WorkflowStatus.Failed;
      context.ErrorMessage = errorMessage;
      WorkflowFailed?.Invoke(this, context);
      ErrorLogger.LogError(
        $"Workflow failed: {context.WorkflowId} - {errorMessage}",
        "WorkflowCoordinatorService");
    }

    #endregion

    public void Dispose()
    {
      if (!_disposed)
      {
        _disposed = true;
      }
    }
  }
}
