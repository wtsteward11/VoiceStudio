using System;
using System.IO;

namespace VoiceStudio.App.Tests.Helpers
{
  /// <summary>
  /// Canonical OpenVoice live-proof reference audio (Policy A — speech-like, not 440 Hz tone).
  /// See <c>docs/design/VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md</c>.
  /// </summary>
  public static class OpenVoiceProofFixtures
  {
    public const string OpenVoiceReferenceSpeechRelativePath = "tests/fixtures/audio/openvoice_reference_speech.wav";

    /// <summary>
    /// Resolves the WAV used for OpenVoice <c>preprocess-reference</c> in live proofs.
    /// Override with <c>VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV</c> (absolute path to an existing file).
    /// </summary>
    public static string ResolveOpenVoiceReferenceWavPath(string repoRoot)
    {
      if (string.IsNullOrEmpty(repoRoot))
      {
        throw new ArgumentException("Repository root is required.", nameof(repoRoot));
      }

      var env = Environment.GetEnvironmentVariable("VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV");
      if (!string.IsNullOrWhiteSpace(env))
      {
        var p = env.Trim().Trim('"');
        if (File.Exists(p))
        {
          return p;
        }
      }

      return Path.Combine(repoRoot, OpenVoiceReferenceSpeechRelativePath);
    }
  }
}
