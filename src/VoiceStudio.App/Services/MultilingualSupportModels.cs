using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Multilingual API models. Matches backend /api/multilingual.
  /// </summary>
  public class SupportedLanguagesResponse
  {
    public LanguageInfo[] Languages { get; set; } = System.Array.Empty<LanguageInfo>();
  }

  public class LanguageInfo
  {
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
  }

  public class TranslationResponse
  {
    public string TranslatedText { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public double Confidence { get; set; }
  }

  public class MultilingualSynthesisRequest
  {
    public string Text { get; set; } = string.Empty;
    public string? SourceLanguage { get; set; }
    public string[] TargetLanguages { get; set; } = System.Array.Empty<string>();
    public Dictionary<string, string> ProfileIds { get; set; } = new();
    public bool PreserveEmotion { get; set; } = true;
    public bool PreserveStyle { get; set; } = true;
  }

  public class MultilingualSynthesisResponse
  {
    public Dictionary<string, string> AudioIds { get; set; } = new();
    public string? DetectedLanguage { get; set; }
    public string Message { get; set; } = string.Empty;
  }
}
