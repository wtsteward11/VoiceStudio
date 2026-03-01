using System;

namespace VoiceStudio.App.Core.Commands
{
    /// <summary>
    /// Represents the execution status of a command.
    /// </summary>
    public enum CommandStatus
    {
        /// <summary>Status has not been determined.</summary>
        Unknown,
        /// <summary>Command is registered and functioning correctly.</summary>
        Working,
        /// <summary>Command is registered but has execution errors.</summary>
        Broken,
        /// <summary>Command is explicitly disabled.</summary>
        Disabled
    }

    /// <summary>
    /// Metadata describing a registered command.
    /// Used for command discovery, documentation, and UI binding.
    /// </summary>
    public sealed class CommandDescriptor
    {
        /// <summary>
        /// Unique identifier for the command (e.g., "file.save", "profile.create").
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// Human-readable title for display in UI.
        /// </summary>
        public required string Title { get; init; }

        /// <summary>
        /// Optional detailed description of what the command does.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Category for grouping commands (e.g., "File", "Profile", "Playback").
        /// </summary>
        public string Category { get; init; } = "General";

        /// <summary>
        /// Icon glyph or emoji for UI display.
        /// </summary>
        public string? Icon { get; init; }

        /// <summary>
        /// Keyboard shortcut string (e.g., "Ctrl+S").
        /// </summary>
        public string? KeyboardShortcut { get; init; }

        /// <summary>
        /// Whether the command is currently enabled.
        /// </summary>
        public bool IsEnabled { get; init; } = true;

        /// <summary>
        /// Whether the command bypasses the busy-state check.
        /// GAP-B12: Commands with BypassBusy=true execute immediately even when IsBusy=true.
        /// Use for critical commands like "Stop", "Cancel", etc.
        /// </summary>
        public bool BypassBusy { get; init; } = false;
    }

    /// <summary>
    /// Runtime state tracking for a command, including execution history.
    /// </summary>
    public sealed class CommandRuntimeState
    {
        /// <summary>
        /// The command descriptor this state belongs to.
        /// </summary>
        public required CommandDescriptor Descriptor { get; init; }

        /// <summary>
        /// Current status of the command.
        /// </summary>
        public CommandStatus Status { get; set; } = CommandStatus.Unknown;

        /// <summary>
        /// Timestamp of last successful execution.
        /// </summary>
        public DateTime? LastExecuted { get; set; }

        /// <summary>
        /// Last error message if the command failed.
        /// </summary>
        public string? LastError { get; set; }

        /// <summary>
        /// Count of successful executions.
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Count of failed executions.
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Average execution duration in milliseconds.
        /// </summary>
        public double AverageExecutionMs { get; set; }
    }

    /// <summary>
    /// Event arguments for command execution events.
    /// </summary>
    public sealed class CommandExecutedEventArgs : EventArgs
    {
        public required string CommandId { get; init; }
        public object? Parameter { get; init; }
        public TimeSpan Duration { get; init; }
    }

    /// <summary>
    /// Event arguments for command failure events.
    /// </summary>
    public sealed class CommandFailedEventArgs : EventArgs
    {
        public required string CommandId { get; init; }
        public object? Parameter { get; init; }
        public required Exception Exception { get; init; }
    }
}
