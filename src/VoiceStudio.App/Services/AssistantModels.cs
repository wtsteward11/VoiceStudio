using System;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Assistant API conversation response.
  /// </summary>
  public class AssistantConversation
  {
    public string ConversationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public AssistantMessage[] Messages { get; set; } = Array.Empty<AssistantMessage>();
    public string Created { get; set; } = string.Empty;
    public string Updated { get; set; } = string.Empty;
  }

  /// <summary>
  /// Assistant API message response.
  /// </summary>
  public class AssistantMessage
  {
    public string MessageId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string[]? Suggestions { get; set; }
  }

  /// <summary>
  /// Assistant API chat response.
  /// </summary>
  public class AssistantChatResponse
  {
    public string ConversationId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string[] Suggestions { get; set; } = Array.Empty<string>();
    public string Timestamp { get; set; } = string.Empty;
  }

  /// <summary>
  /// Assistant API task suggestion response.
  /// </summary>
  public class AssistantTaskSuggestion
  {
    public string TaskId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public int? EstimatedTime { get; set; }
    public double Confidence { get; set; }
  }
}
