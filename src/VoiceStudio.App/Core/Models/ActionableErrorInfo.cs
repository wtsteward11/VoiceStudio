using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  public enum ActionableErrorSeverity
  {
    Error,
    Warning,
    Info
  }

  public enum ActionableErrorClass
  {
    ValidationInput,
    CapabilityUnsupported,
    EnvironmentUnavailable,
    TransientRetryable,
    Unknown
  }

  public enum ActionableOperationContext
  {
    General,
    VoiceSynthesize,
    SSMLPreview,
    SSMLValidate
  }

  public sealed class ActionableErrorInfo
  {
    public string Title { get; init; } = string.Empty;
    public string PrimaryMessage { get; init; } = string.Empty;
    public string? SecondaryDetail { get; init; }
    public string RecommendedAction { get; init; } = string.Empty;
    public ActionableErrorSeverity Severity { get; init; } = ActionableErrorSeverity.Error;
    public ActionableErrorClass Class { get; init; } = ActionableErrorClass.Unknown;
    public bool IsRetryable { get; init; }
    public IReadOnlyList<string>? Warnings { get; init; }
  }
}
