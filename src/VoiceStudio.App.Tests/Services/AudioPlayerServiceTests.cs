using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services
{
  [TestClass]
  [TestCategory("Services")]
  public class AudioPlayerServiceTests
  {
    private sealed class FakeWavHttpHandler : HttpMessageHandler
    {
      private readonly byte[] _wavBytes;

      public FakeWavHttpHandler(byte[] wavBytes)
      {
        _wavBytes = wavBytes ?? throw new ArgumentNullException(nameof(wavBytes));
      }

      protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
      {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new ByteArrayContent(_wavBytes);
        response.Content.Headers.ContentType =
          new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        return Task.FromResult(response);
      }
    }

    private static int CountVoicestudioTempFiles()
    {
      var tempDir = Path.GetTempPath();
      var files = Directory.GetFiles(tempDir, "voicestudio_*");
      return files.Length;
    }

    [TestMethod]
    public async Task PlayUrlAsync_Stop_DeletesTempFile()
    {
      var wavBytes = MinimalWavHelper.CreateMinimalWavBytes();
      var handler = new FakeWavHttpHandler(wavBytes);
      var httpClient = new HttpClient(handler);
      using var service = new AudioPlayerService(httpClient);

      var before = CountVoicestudioTempFiles();
      var playTask = service.PlayUrlAsync("http://fake/audio.wav");

      await Task.Delay(300);
      service.Stop();
      try
      {
        await playTask;
      }
      catch
      {
        // Ignore - playback may throw when stopped
      }

      await Task.Delay(100);
      var after = CountVoicestudioTempFiles();
      Assert.IsTrue(after <= before, "Temp file should be deleted after Stop()");
    }

    [TestMethod]
    public async Task PlayUrlAsync_NormalCompletion_DeletesTempFile()
    {
      var testTemp = Path.Combine(Path.GetTempPath(), "voicestudio_test_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(testTemp);
      var oldTmp = Environment.GetEnvironmentVariable("TMP");
      var oldTemp = Environment.GetEnvironmentVariable("TEMP");
      try
      {
        Environment.SetEnvironmentVariable("TMP", testTemp);
        Environment.SetEnvironmentVariable("TEMP", testTemp);

        var wavBytes = MinimalWavHelper.CreateMinimalWavBytes();
        var handler = new FakeWavHttpHandler(wavBytes);
        var httpClient = new HttpClient(handler);
        using var service = new AudioPlayerService(httpClient);

        var completed = false;
        await service.PlayUrlAsync("http://fake/audio.wav", () => completed = true);
        for (var i = 0; i < 25 && !completed; i++)
          await Task.Delay(100);
        Assert.IsTrue(completed, "Playback should complete");
        await Task.Delay(500);

        var files = Directory.GetFiles(testTemp, "voicestudio_*");
        Assert.AreEqual(0, files.Length, "Temp file should be deleted after normal completion");
      }
      finally
      {
        Environment.SetEnvironmentVariable("TMP", oldTmp ?? string.Empty);
        Environment.SetEnvironmentVariable("TEMP", oldTemp ?? string.Empty);
        try { Directory.Delete(testTemp, true); } catch { /* best effort */ }
      }
    }

    [TestMethod]
    public async Task PlayUrlAsync_Dispose_DeletesTempFile()
    {
      var wavBytes = MinimalWavHelper.CreateMinimalWavBytes();
      var handler = new FakeWavHttpHandler(wavBytes);
      var httpClient = new HttpClient(handler);
      var service = new AudioPlayerService(httpClient);

      var before = CountVoicestudioTempFiles();
      var playTask = service.PlayUrlAsync("http://fake/audio.wav");

      await Task.Delay(200);
      service.Dispose();
      try
      {
        await playTask;
      }
      catch
      {
        // Ignore
      }

      await Task.Delay(100);
      var after = CountVoicestudioTempFiles();
      Assert.IsTrue(after <= before, "Temp file should be deleted after Dispose()");
    }

    [TestMethod]
    public async Task PlayUrlAsync_StreamingDownload_CreatesPlayableFile()
    {
      var wavBytes = MinimalWavHelper.CreateMinimalWavBytes();
      var handler = new FakeWavHttpHandler(wavBytes);
      var httpClient = new HttpClient(handler);
      using var service = new AudioPlayerService(httpClient);

      var completed = false;
      await service.PlayUrlAsync("http://fake/audio.wav", () => completed = true);
      await Task.Delay(800);
      Assert.IsTrue(completed, "Playback should complete and invoke callback");
    }
  }
}
