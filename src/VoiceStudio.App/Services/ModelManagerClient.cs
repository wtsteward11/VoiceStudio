using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/models. PR-15: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class ModelManagerClient : IModelManagerClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with pipeline from CreateModelManagerClient.
    /// </summary>
    internal ModelManagerClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<List<ModelInfo>> GetModelsAsync(string? engine = null, CancellationToken cancellationToken = default)
    {
      var url = "/api/models";
      if (!string.IsNullOrEmpty(engine))
      {
        url += $"?engine={Uri.EscapeDataString(engine)}";
      }
      var result = await _pipeline.GetAsync<List<ModelInfo>>(url, cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize models");
    }

    /// <inheritdoc />
    public async Task<ModelInfo> GetModelAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      var path = $"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}";
      var result = await _pipeline.GetAsync<ModelInfo>(path, cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize model");
    }

    /// <inheritdoc />
    public Task<ModelInfo> RegisterModelAsync(string engine, string modelName, string modelPath, string? version = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
    {
      var request = new
      {
        engine,
        model_name = modelName,
        model_path = modelPath,
        version,
        metadata
      };
      return _pipeline.PostAsync<object, ModelInfo>("/api/models", request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ModelVerifyResponse> VerifyModelAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      var path = $"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}/verify";
      var result = await _pipeline.SendRequestAsync<object?, ModelVerifyResponse>(path, null, HttpMethod.Post, cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize verification response");
    }

    /// <inheritdoc />
    public async Task<ModelInfo> UpdateModelChecksumAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      var path = $"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}/update-checksum";
      var result = await _pipeline.SendRequestAsync<object?, ModelInfo>(path, null, HttpMethod.Put, cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize model");
    }

    /// <inheritdoc />
    public async Task<bool> DeleteModelAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      var path = $"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}";
      await _pipeline.SendRequestAsync<object, object>(path, null, HttpMethod.Delete, cancellationToken);
      return true;
    }

    /// <inheritdoc />
    public Task<Stream> ExportModelAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      var path = $"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}/export";
      return _pipeline.GetStreamAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ModelInfo> ImportModelAsync(Stream modelArchive, string? engine = null, CancellationToken cancellationToken = default)
    {
      var queryParams = string.IsNullOrEmpty(engine) ? null : new Dictionary<string, string> { { "engine", engine } };
      var result = await _pipeline.PostMultipartAsync<ModelInfo>(
          "/api/models/import",
          modelArchive,
          "file",
          "model.zip",
          queryParams,
          cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize model");
    }

    /// <inheritdoc />
    public async Task<StorageStats> GetStorageStatsAsync(CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<StorageStats>("/api/models/stats/storage", cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize storage stats");
    }
  }
}
