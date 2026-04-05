using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.UseCases
{
  /// <summary>
  /// Implementation of timeline use case.
  /// Encapsulates all timeline-related business logic.
  /// </summary>
  public class TimelineUseCase : ITimelineUseCase
  {
    private readonly IBackendClient _backendClient;

    public TimelineUseCase(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
    }

    public async Task<TimelineState> GetStateAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _backendClient.GetAsync<TimelineState>("/api/timeline/state", cancellationToken);
        return response ?? new TimelineState();
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to get timeline state: {ex.Message}", "TimelineUseCase");
        return new TimelineState();
      }
    }

    public async Task<TimelineState> CreateAsync(TimelineOptions options, CancellationToken cancellationToken = default)
    {
      var response = await _backendClient.PostAsync<TimelineOptions, TimelineState>(
          "/api/timeline/create", options, cancellationToken);
      return response ?? throw new InvalidOperationException("Failed to create timeline");
    }

    public async Task<Track> AddTrackAsync(TrackType type, string? name = null, CancellationToken cancellationToken = default)
    {
      var request = new { Type = type.ToString(), Name = name };
      var response = await _backendClient.PostAsync<object, Track>("/api/timeline/tracks", request, cancellationToken);
      return response ?? throw new InvalidOperationException("Failed to add track");
    }

    public async Task<bool> RemoveTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
      var request = new DeleteTimelineEntityRequest { Id = trackId };
      var response = await _backendClient.PostAsync<DeleteTimelineEntityRequest, DeleteResponse>($"/api/timeline/tracks/delete", request, cancellationToken);
      return response?.Success ?? false;
    }

    public async Task<Clip> AddClipAsync(string trackId, ClipData clipData, double startTime, CancellationToken cancellationToken = default)
    {
      var request = new AddTimelineClipApiRequest
      {
        TrackId = trackId,
        SourcePath = clipData.SourcePath,
        StartTime = startTime,
        Duration = clipData.Duration > 0 ? clipData.Duration : 1.0,
        Name = string.IsNullOrWhiteSpace(clipData.Name) ? "Clip" : clipData.Name,
      };
      var response = await _backendClient.PostAsync<AddTimelineClipApiRequest, Clip>("/api/timeline/clips", request, cancellationToken);
      var clip = response ?? throw new InvalidOperationException("Failed to add clip");
      NormalizeClipFromApi(clip);
      return clip;
    }

    public async Task<bool> RemoveClipAsync(string clipId, CancellationToken cancellationToken = default)
    {
      var request = new DeleteTimelineEntityRequest { Id = clipId };
      var response = await _backendClient.PostAsync<DeleteTimelineEntityRequest, DeleteResponse>($"/api/timeline/clips/delete", request, cancellationToken);
      return response?.Success ?? false;
    }

    public async Task<Clip> MoveClipAsync(string clipId, double newStartTime, string? newTrackId = null, CancellationToken cancellationToken = default)
    {
      var encoded = Uri.EscapeDataString(clipId);
      var request = new MoveClipApiRequest { NewStartTime = newStartTime, NewTrackId = newTrackId };
      var response = await _backendClient.PutAsync<MoveClipApiRequest, Clip>($"/api/timeline/clips/{encoded}/move", request, cancellationToken);
      var clip = response ?? throw new InvalidOperationException("Failed to move clip");
      NormalizeClipFromApi(clip);
      return clip;
    }

    public async Task<Clip> TrimClipAsync(string clipId, double trimStart, double trimEnd, CancellationToken cancellationToken = default)
    {
      var encoded = Uri.EscapeDataString(clipId);
      var request = new TrimClipApiRequest { NewStart = trimStart, NewEnd = trimEnd };
      var response = await _backendClient.PutAsync<TrimClipApiRequest, Clip>($"/api/timeline/clips/{encoded}/trim", request, cancellationToken);
      var clip = response ?? throw new InvalidOperationException("Failed to trim clip");
      NormalizeClipFromApi(clip);
      return clip;
    }

    public async Task<(Clip left, Clip right)> SplitClipAsync(string clipId, double splitTime, CancellationToken cancellationToken = default)
    {
      var encoded = Uri.EscapeDataString(clipId);
      var request = new SplitClipApiRequest { SplitPosition = splitTime };
      var response = await _backendClient.PostAsync<SplitClipApiRequest, SplitClipApiResponse>($"/api/timeline/clips/{encoded}/split", request, cancellationToken);

      if (response?.ClipBefore == null || response.ClipAfter == null)
        throw new InvalidOperationException("Failed to split clip");

      NormalizeClipFromApi(response.ClipBefore);
      NormalizeClipFromApi(response.ClipAfter);
      return (response.ClipBefore, response.ClipAfter);
    }

    /// <inheritdoc />
    public async Task<Clip> SetClipFadeAsync(string clipId, double fadeInSeconds, double fadeOutSeconds, CancellationToken cancellationToken = default)
    {
      var encoded = Uri.EscapeDataString(clipId);
      var request = new SetClipFadeApiRequest
      {
        FadeInSeconds = fadeInSeconds,
        FadeOutSeconds = fadeOutSeconds,
      };
      var response = await _backendClient.PutAsync<SetClipFadeApiRequest, Clip>(
          $"/api/timeline/clips/{encoded}/fade",
          request,
          cancellationToken);
      var clip = response ?? throw new InvalidOperationException("Failed to set clip fade");
      NormalizeClipFromApi(clip);
      return clip;
    }

    private static void NormalizeClipFromApi(Clip c)
    {
      if (c.Duration <= 0 && c.EndTimeSeconds > c.StartTime)
        c.Duration = c.EndTimeSeconds - c.StartTime;
    }

    public async Task SetPlayheadAsync(double position, CancellationToken cancellationToken = default)
    {
      await _backendClient.PostAsync<object, object>("/api/timeline/playhead", new { Position = position }, cancellationToken);
    }

    public async Task SetLoopRegionAsync(double start, double end, CancellationToken cancellationToken = default)
    {
      await _backendClient.PostAsync<object, object>("/api/timeline/loop", new { Start = start, End = end }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ImportProjectTimelineAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var trimmed = projectId?.Trim() ?? string.Empty;
      if (string.IsNullOrEmpty(trimmed))
        return;

      _ = await _backendClient.PostAsync<ImportProjectBody, ImportProjectTimelineResponse>(
          "/api/timeline/import-from-project",
          new ImportProjectBody { ProjectId = trimmed },
          cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateTimelineTrackAsync(
        string trackId,
        bool? isMuted = null,
        bool? isSolo = null,
        CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(trackId))
        throw new ArgumentException("Track id is required.", nameof(trackId));

      var encoded = Uri.EscapeDataString(trackId.Trim());
      var body = new UpdateTimelineTrackApiRequest
      {
        Muted = isMuted,
        Solo = isSolo,
      };

      _ = await _backendClient.PutAsync<UpdateTimelineTrackApiRequest, Track>(
          $"/api/timeline/tracks/{encoded}",
          body,
          cancellationToken);
    }

    public async Task<string> ExportAsync(string outputPath, ExportOptions options, CancellationToken cancellationToken = default)
    {
      if (!string.IsNullOrWhiteSpace(options.ProjectId))
      {
        await ImportProjectTimelineAsync(options.ProjectId!, cancellationToken).ConfigureAwait(false);
      }

      var body = new TimelineExportApiRequest
      {
        OutputPath = outputPath,
        Format = string.IsNullOrWhiteSpace(options.Format) ? "wav" : options.Format,
        SampleRate = options.SampleRate > 0 ? options.SampleRate : null,
        ProjectId = options.ProjectId,
        ApplyEffects = options.ApplyEffectsDuringExport,
        EffectChainId = options.EffectChainId,
        FallbackProjectAudioId = options.FallbackProjectAudioId,
        LufsPreset = string.IsNullOrWhiteSpace(options.LufsPreset) ? "podcast_stereo" : options.LufsPreset!.Trim(),
      };

      try
      {
        var response = await _backendClient.PostAsync<TimelineExportApiRequest, TimelineExportResponseDto>(
            "/api/timeline/export",
            body,
            cancellationToken);

        if (response == null || !response.Success || string.IsNullOrWhiteSpace(response.OutputPath))
          throw new InvalidOperationException("Timeline export failed or returned no output path.");

        return response.OutputPath;
      }
      catch (BackendValidationException ex)
      {
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(ex.Message)
                ? "Cannot export: the timeline has no audible audio. Add clips or use a valid fallback."
                : ex.Message,
            ex);
      }
    }

    public async Task<bool> UndoAsync(CancellationToken cancellationToken = default)
    {
      var response = await _backendClient.PostAsync<object, UndoResponse>("/api/timeline/undo", new { }, cancellationToken);
      return response?.Success ?? false;
    }

    public async Task<bool> RedoAsync(CancellationToken cancellationToken = default)
    {
      var response = await _backendClient.PostAsync<object, UndoResponse>("/api/timeline/redo", new { }, cancellationToken);
      return response?.Success ?? false;
    }

    public async Task<UndoRedoState> GetUndoRedoStateAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _backendClient.GetAsync<UndoRedoState>("/api/timeline/undo-redo-state", cancellationToken);
        return response ?? new UndoRedoState();
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to get undo/redo state: {ex.Message}", "TimelineUseCase");
        return new UndoRedoState();
      }
    }

    public class ExportResponse { public string? OutputPath { get; set; } }

    public sealed class DeleteTimelineEntityRequest
    {
      public string Id { get; set; } = "";
    }

    public sealed class AddTimelineClipApiRequest
    {
      public string TrackId { get; set; } = "";
      public string? SourcePath { get; set; }
      public double StartTime { get; set; }
      public double Duration { get; set; }
      public string? Name { get; set; }
    }

    public sealed class MoveClipApiRequest
    {
      public double NewStartTime { get; set; }
      public string? NewTrackId { get; set; }
    }

    public sealed class TrimClipApiRequest
    {
      public double NewStart { get; set; }
      public double NewEnd { get; set; }
    }

    public sealed class SplitClipApiRequest
    {
      public double SplitPosition { get; set; }
    }

    public sealed class SplitClipApiResponse
    {
      [JsonPropertyName("clip_before")]
      public Clip ClipBefore { get; set; } = default!;

      [JsonPropertyName("clip_after")]
      public Clip ClipAfter { get; set; } = default!;
    }

    public sealed class SetClipFadeApiRequest
    {
      [JsonPropertyName("fade_in_seconds")]
      public double FadeInSeconds { get; set; }

      [JsonPropertyName("fade_out_seconds")]
      public double FadeOutSeconds { get; set; }
    }

    /// <summary>Serializable body for <c>PUT /api/timeline/tracks/{id}</c> (mix fields).</summary>
    public sealed class UpdateTimelineTrackApiRequest
    {
      public bool? Muted { get; set; }

      public bool? Solo { get; set; }
    }

    /// <summary>Serializable body for <c>POST /api/timeline/import-from-project</c>.</summary>
    public sealed class ImportProjectBody
    {
      public string ProjectId { get; set; } = "";
    }

    /// <summary>Response shape for import-from-project (only <see cref="Id"/> required for fire-and-forget).</summary>
    public sealed class ImportProjectTimelineResponse
    {
      public string? Id { get; set; }
    }

    /// <summary>Serializable body for <c>POST /api/timeline/export</c> (snake_case via JSON options).</summary>
    public sealed class TimelineExportApiRequest
    {
      public string OutputPath { get; set; } = "";
      public string Format { get; set; } = "wav";
      public int? SampleRate { get; set; }
      public string? ProjectId { get; set; }
      public bool ApplyEffects { get; set; }
      public string? EffectChainId { get; set; }
      public string? FallbackProjectAudioId { get; set; }
      public string LufsPreset { get; set; } = "podcast_stereo";
    }

    public sealed class TimelineExportResponseDto
    {
      public bool Success { get; set; }
      public string? OutputPath { get; set; }
      public double Duration { get; set; }
    }
    public class UndoResponse { public bool Success { get; set; } }
    public class DeleteResponse { public bool Success { get; set; } }
  }
}
