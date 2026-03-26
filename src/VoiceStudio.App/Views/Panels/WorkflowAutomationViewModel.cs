using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Workflow Automation UI.
  /// Implements IDEA 33: Workflow Automation UI.
  /// </summary>
  public partial class WorkflowAutomationViewModel : ObservableObject
  {
    private readonly IWorkflowAutomationClient _workflowClient;
    private string? _currentWorkflowId;

    [ObservableProperty]
    private ObservableCollection<WorkflowStep> workflowSteps = new();

    [ObservableProperty]
    private WorkflowStep? selectedStep;

    [ObservableProperty]
    private ObservableCollection<WorkflowVariable> variables = new();

    [ObservableProperty]
    private ObservableCollection<WorkflowTemplate> templates = new();

    [ObservableProperty]
    private string workflowName = string.Empty;

    [ObservableProperty]
    private string workflowDescription = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private WorkflowExecutionResult? lastExecutionResult;

    public bool HasSelectedStep => SelectedStep != null;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public WorkflowAutomationViewModel(IWorkflowAutomationClient workflowClient)
    {
      _workflowClient = workflowClient ?? throw new ArgumentNullException(nameof(workflowClient));
      CreateWorkflowCommand = new RelayCommand(CreateWorkflow, () => !string.IsNullOrWhiteSpace(WorkflowName));
      SaveWorkflowCommand = new AsyncRelayCommand(SaveWorkflowAsync, () => !string.IsNullOrWhiteSpace(WorkflowName) && WorkflowSteps.Count > 0);
      TestWorkflowCommand = new AsyncRelayCommand(TestWorkflowAsync, () => WorkflowSteps.Count > 0);
      RunWorkflowCommand = new AsyncRelayCommand(RunWorkflowAsync, () => WorkflowSteps.Count > 0);

      LoadTemplates();
    }

    public IRelayCommand CreateWorkflowCommand { get; }
    public IAsyncRelayCommand SaveWorkflowCommand { get; }
    public IAsyncRelayCommand TestWorkflowCommand { get; }
    public IAsyncRelayCommand RunWorkflowCommand { get; }

    partial void OnWorkflowNameChanged(string value)
    {
      CreateWorkflowCommand.NotifyCanExecuteChanged();
      SaveWorkflowCommand.NotifyCanExecuteChanged();
    }

    partial void OnWorkflowStepsChanged(ObservableCollection<WorkflowStep> value)
    {
      SaveWorkflowCommand.NotifyCanExecuteChanged();
      TestWorkflowCommand.NotifyCanExecuteChanged();
      RunWorkflowCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedStepChanged(WorkflowStep? value)
    {
      OnPropertyChanged(nameof(HasSelectedStep));
    }

    private void LoadTemplates()
    {
      Templates.Clear();
      Templates.Add(new WorkflowTemplate
      {
        Id = "batch_export",
        Name = "Batch Export",
        Description = "Synthesize multiple texts and export as audio files"
      });
      Templates.Add(new WorkflowTemplate
      {
        Id = "quality_check",
        Name = "Quality Check",
        Description = "Synthesize, analyze quality, and apply enhancements"
      });
      Templates.Add(new WorkflowTemplate
      {
        Id = "effect_processing",
        Name = "Effect Processing",
        Description = "Apply effects chain to multiple audio clips"
      });
    }

    private void CreateWorkflow()
    {
      WorkflowSteps.Clear();
      Variables.Clear();
      WorkflowName = string.Empty;
      WorkflowDescription = string.Empty;
      SelectedStep = null;
      _currentWorkflowId = null;
      LastExecutionResult = null;
      ErrorMessage = null;
      StatusMessage = null;
    }

    public void AddStep(string actionType, string actionName)
    {
      var step = new WorkflowStep
      {
        Id = Guid.NewGuid().ToString(),
        Type = actionType,
        Name = actionName,
        Properties = new Dictionary<string, object>(),
        Order = WorkflowSteps.Count
      };

      WorkflowSteps.Add(step);
      SelectedStep = step;
    }

    public void RemoveStep(WorkflowStep step)
    {
      WorkflowSteps.Remove(step);
      // Reorder remaining steps
      for (int i = 0; i < WorkflowSteps.Count; i++)
      {
        WorkflowSteps[i].Order = i;
      }
      if (SelectedStep == step)
      {
        SelectedStep = null;
      }
    }

    public void AddVariable(string name, string value)
    {
      if (!Variables.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
      {
        Variables.Add(new WorkflowVariable
        {
          Name = name,
          Value = value
        });
      }
    }

    public void RemoveVariable(WorkflowVariable variable)
    {
      Variables.Remove(variable);
    }

    private async Task SaveWorkflowAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Saving workflow...";

        var request = new WorkflowCreateRequest
        {
          Name = WorkflowName,
          Description = WorkflowDescription,
          Steps = WorkflowSteps.ToList(),
          Variables = Variables.ToList(),
          IsEnabled = true
        };

        Workflow workflow;
        if (!string.IsNullOrEmpty(_currentWorkflowId))
        {
          // Update existing workflow
          var updateRequest = new WorkflowUpdateRequest
          {
            Name = WorkflowName,
            Description = WorkflowDescription,
            Steps = WorkflowSteps.ToList(),
            Variables = Variables.ToList()
          };
          workflow = await _workflowClient.UpdateWorkflowAsync(_currentWorkflowId, updateRequest);
        }
        else
        {
          // Create new workflow
          workflow = await _workflowClient.CreateWorkflowAsync(request);
          _currentWorkflowId = workflow.Id;
        }

        StatusMessage = $"Workflow '{workflow.Name}' saved successfully";
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to save workflow: {ex.Message}";
        StatusMessage = null;
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task TestWorkflowAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Testing workflow with sample data...";

        // Save workflow first if not saved
        if (string.IsNullOrEmpty(_currentWorkflowId))
        {
          await SaveWorkflowAsync();
          if (!string.IsNullOrEmpty(ErrorMessage))
          {
            return; // Save failed
          }
        }

        // Prepare test input data
        var testInputData = new Dictionary<string, object>();
        foreach (var variable in Variables)
        {
          testInputData[variable.Name] = variable.Value;
        }

        // Execute workflow with test data
        var result = await _workflowClient.ExecuteWorkflowAsync(_currentWorkflowId!, testInputData);
        LastExecutionResult = result;

        if (result.Status == "completed")
        {
          StatusMessage = $"Workflow test completed successfully. Progress: {result.Progress:P0}";
        }
        else
        {
          ErrorMessage = $"Workflow test failed: {result.ErrorMessage ?? "Unknown error"}";
          StatusMessage = null;
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to test workflow: {ex.Message}";
        StatusMessage = null;
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RunWorkflowAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Executing workflow...";

        // Save workflow first if not saved
        if (string.IsNullOrEmpty(_currentWorkflowId))
        {
          await SaveWorkflowAsync();
          if (!string.IsNullOrEmpty(ErrorMessage))
          {
            return; // Save failed
          }
        }

        // Prepare input data from variables
        var inputData = new Dictionary<string, object>();
        foreach (var variable in Variables)
        {
          inputData[variable.Name] = variable.Value;
        }

        // Execute workflow
        var result = await _workflowClient.ExecuteWorkflowAsync(_currentWorkflowId!, inputData);
        LastExecutionResult = result;

        if (result.Status == "completed")
        {
          StatusMessage = $"Workflow executed successfully. Progress: {result.Progress:P0}";
        }
        else
        {
          ErrorMessage = $"Workflow execution failed: {result.ErrorMessage ?? "Unknown error"}";
          StatusMessage = null;
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to execute workflow: {ex.Message}";
        StatusMessage = null;
      }
      finally
      {
        IsLoading = false;
      }
    }
  }

  public class WorkflowTemplate
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
  }
}