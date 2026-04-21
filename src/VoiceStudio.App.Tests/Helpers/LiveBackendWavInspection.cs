using System;
using System.Text;

namespace VoiceStudio.App.Tests.Helpers;

/// <summary>
/// RIFF/WAV inspection for live-backend synthesis proofs. Chatterbox worker may emit
/// IEEE float (WAVE_FORMAT_IEEE_FLOAT / 32-bit) WAV; std PCM16 is also supported.
/// </summary>
internal static class LiveBackendWavInspection
{
  /// <summary>WAVE_FORMAT_PCM</summary>
  internal const ushort WAVE_FORMAT_PCM = 1;

  /// <summary>WAVE_FORMAT_IEEE_FLOAT</summary>
  internal const ushort WAVE_FORMAT_IEEE_FLOAT = 3;

  internal static void GetWavAudioLayout(
    byte[] wav,
    out ushort wFormatTag,
    out int channels,
    out int sampleRate,
    out int bitsPerSample,
    out int pcmStart,
    out int pcmLength)
  {
    wFormatTag = WAVE_FORMAT_PCM;
    channels = 0;
    sampleRate = 0;
    bitsPerSample = 16;
    pcmStart = 0;
    pcmLength = 0;
    if (wav.Length < 12)
    {
      throw new InvalidOperationException("WAV too short for RIFF header.");
    }

    var pos = 12;
    while (pos + 8 <= wav.Length)
    {
      var id = Encoding.ASCII.GetString(wav, pos, 4);
      var size = wav[pos + 4] | (wav[pos + 5] << 8) | (wav[pos + 6] << 16) | (wav[pos + 7] << 24);
      pos += 8;
      if (id.Equals("fmt ", StringComparison.Ordinal) && size >= 16 && pos + 16 <= wav.Length)
      {
        wFormatTag = (ushort)(wav[pos] | (wav[pos + 1] << 8));
        channels = wav[pos + 2] | (wav[pos + 3] << 8);
        sampleRate = wav[pos + 4] | (wav[pos + 5] << 8) | (wav[pos + 6] << 16) | (wav[pos + 7] << 24);
        bitsPerSample = wav[pos + 14] | (wav[pos + 15] << 8);
      }
      else if (id.Equals("data", StringComparison.Ordinal))
      {
        pcmStart = pos;
        pcmLength = size;
        return;
      }

      pos += size;
      if (size % 2 == 1)
      {
        pos++;
      }
    }

    throw new InvalidOperationException("Could not locate fmt and/or data chunks.");
  }

  /// <summary>
  /// Peak on the same scale as 16-bit PCM max abs sample (0..32767) so existing thresholds apply.
  /// </summary>
  internal static int ComputePeakInt16Equivalent(
    byte[] wav,
    ushort wFormatTag,
    int bitsPerSample,
    int pcmStart,
    int pcmLength)
  {
    if (wFormatTag == WAVE_FORMAT_PCM && bitsPerSample == 16)
    {
      return MaxAbsPcm16Le(wav, pcmStart, pcmLength);
    }

    if (wFormatTag == WAVE_FORMAT_IEEE_FLOAT && bitsPerSample == 32)
    {
      return MaxAbsIeeeFloat32PeakAsInt16Equivalent(wav, pcmStart, pcmLength);
    }

    throw new InvalidOperationException(
      $"Unsupported WAV for live proof: wFormatTag={wFormatTag}, bitsPerSample={bitsPerSample}");
  }

  private static int MaxAbsPcm16Le(byte[] buf, int start, int len)
  {
    var max = 0;
    var end = Math.Min(buf.Length, start + len);
    for (var i = start; i + 1 < end; i += 2)
    {
      var s = (short)(buf[i] | (buf[i + 1] << 8));
      var v = s == short.MinValue ? 32767 : Math.Abs((int)s);
      if (v > max)
      {
        max = v;
      }
    }

    return max;
  }

  private static int MaxAbsIeeeFloat32PeakAsInt16Equivalent(byte[] buf, int start, int len)
  {
    var maxAbs = 0f;
    var end = Math.Min(buf.Length, start + len);
    for (var i = start; i + 3 < end; i += 4)
    {
      var f = BitConverter.ToSingle(buf, i);
      var a = Math.Abs(f);
      if (a > maxAbs)
      {
        maxAbs = a;
      }
    }

    return (int)Math.Min(maxAbs * 32767.0, int.MaxValue);
  }
}
