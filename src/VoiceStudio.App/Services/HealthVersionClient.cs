using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/health and /api/version. PR-5: extracted from BackendClient; uses BackendClientHttpPipeline.
  /// </summary>
  public sealed class HealthVersionClient : IHealthVersionClient
  {
    private const string ExpectedApiVersion = "v2";
    private const string MinimumApiVersion = "v1";

    private readonly BackendClientHttpPipeline _pipeline;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    internal HealthVersionClient(BackendHttpContext httpContext)
    {
      if (httpContext == null)
        throw new ArgumentNullException(nameof(httpContext));
      _pipeline = httpContext.Pipeline;
      _httpClient = httpContext.HttpClient;
      _jsonOptions = JsonSerializerOptionsFactory.BackendApi;
    }

    /// <inheritdoc />
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
      => _pipeline.CheckHealthAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ApiVersionCheckResult> CheckApiVersionAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _httpClient.GetAsync("/api/version/compatibility", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          return new ApiVersionCheckResult
          {
            IsCompatible = true,
            ServerVersion = "unknown",
            ClientVersion = ExpectedApiVersion,
            Message = "Version endpoint not available. Assuming compatible."
          };
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var serverVersion = root.TryGetProperty("server_version", out var sv) ? sv.GetString() ?? "unknown" : "unknown";
        var isCompatible = root.TryGetProperty("compatible", out var compat) && compat.GetBoolean();
        var supportedVersions = new List<string>();

        if (root.TryGetProperty("supported_versions", out var supported) && supported.ValueKind == JsonValueKind.Array)
        {
          foreach (var v in supported.EnumerateArray())
          {
            if (v.ValueKind == JsonValueKind.String)
            {
              supportedVersions.Add(v.GetString() ?? "");
            }
          }
        }

        string? recommendation = null;
        if (root.TryGetProperty("recommendation", out var rec) && rec.ValueKind == JsonValueKind.String)
        {
          recommendation = rec.GetString();
        }

        var clientVersionSupported = supportedVersions.Contains(ExpectedApiVersion) ||
                                     supportedVersions.Contains(MinimumApiVersion);

        var message = isCompatible
          ? $"API version compatible. Server: {serverVersion}, Client: {ExpectedApiVersion}"
          : $"API version mismatch. Server: {serverVersion}, Client expected: {ExpectedApiVersion}";

        return new ApiVersionCheckResult
        {
          IsCompatible = isCompatible && clientVersionSupported,
          ServerVersion = serverVersion,
          ClientVersion = ExpectedApiVersion,
          SupportedVersions = supportedVersions,
          Message = message,
          Recommendation = recommendation
        };
      }
      catch (Exception ex)
      {
        return new ApiVersionCheckResult
        {
          IsCompatible = true,
          ServerVersion = "unknown",
          ClientVersion = ExpectedApiVersion,
          Message = $"Version check failed: {ex.Message}",
          Error = ex.Message
        };
      }
    }

    /// <inheritdoc />
    public async Task<ApiVersionInfo?> GetApiVersionInfoAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _httpClient.GetAsync("/api/version/", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var currentVersion = root.TryGetProperty("current_version", out var cv) ? cv.GetString() : null;
        var defaultVersion = root.TryGetProperty("default_version", out var dv) ? dv.GetString() : null;
        var supportedVersions = new List<string>();

        if (root.TryGetProperty("supported_versions", out var supported) && supported.ValueKind == JsonValueKind.Array)
        {
          foreach (var v in supported.EnumerateArray())
          {
            if (v.ValueKind == JsonValueKind.String)
            {
              supportedVersions.Add(v.GetString() ?? "");
            }
          }
        }

        return new ApiVersionInfo
        {
          CurrentVersion = currentVersion ?? "unknown",
          DefaultVersion = defaultVersion ?? "unknown",
          SupportedVersions = supportedVersions
        };
      }
      catch
      {
        return null;
      }
    }

    /// <inheritdoc />
    public async Task<bool> ValidateApiVersionOnStartupAsync(CancellationToken cancellationToken = default)
    {
      var result = await CheckApiVersionAsync(cancellationToken);

      if (!result.IsCompatible)
      {
        System.Diagnostics.Debug.WriteLine(
          $"[WARNING] API version mismatch: {result.Message}. " +
          $"Recommendation: {result.Recommendation ?? "Update client"}");
        return false;
      }

      if (!string.IsNullOrEmpty(result.Recommendation))
      {
        System.Diagnostics.Debug.WriteLine($"[INFO] API version note: {result.Recommendation}");
      }

      return true;
    }
  }
}
