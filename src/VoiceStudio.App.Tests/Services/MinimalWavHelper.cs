using System;
using System.Text;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Generates minimal valid WAV bytes for testing.
  /// 22050 Hz mono 16-bit PCM, ~0.5 second duration.
  /// </summary>
  internal static class MinimalWavHelper
  {
    private const int SampleRate = 22050;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const double DurationSeconds = 0.5;

    public static byte[] CreateMinimalWavBytes()
    {
      var numSamples = (int)(SampleRate * Channels * DurationSeconds);
      var dataSize = numSamples * (BitsPerSample / 8);
      var fileSize = 36 + dataSize;

      var buffer = new byte[44 + dataSize];
      var offset = 0;

      void Write(byte[] bytes)
      {
        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
        offset += bytes.Length;
      }

      Write(Encoding.ASCII.GetBytes("RIFF"));
      Write(BitConverter.GetBytes(fileSize));
      Write(Encoding.ASCII.GetBytes("WAVE"));
      Write(Encoding.ASCII.GetBytes("fmt "));
      Write(BitConverter.GetBytes(16));
      Write(BitConverter.GetBytes((ushort)1));
      Write(BitConverter.GetBytes((ushort)Channels));
      Write(BitConverter.GetBytes(SampleRate));
      Write(BitConverter.GetBytes(SampleRate * Channels * (BitsPerSample / 8)));
      Write(BitConverter.GetBytes((ushort)(Channels * (BitsPerSample / 8))));
      Write(BitConverter.GetBytes((ushort)BitsPerSample));
      Write(Encoding.ASCII.GetBytes("data"));
      Write(BitConverter.GetBytes(dataSize));
      // Data bytes remain zero (silence)

      return buffer;
    }
  }
}
