using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/automation. Thin pass-through to IBackendClient.SendRequestAsync.
  /// </summary>
  public sealed class AutomationClient : IAutomationClient
  {
    private readonly IBackendClient _backend;

    public AutomationClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<AutomationTrackInfo[]> GetTracksAsync(CancellationToken cancellationToken = default)
    {
      var result = await _backend.SendRequestAsync<object, AutomationTrackInfo[]>(
          "/api/automation/tracks",
          null,
          HttpMethod.Get,
          cancellationToken);
      return result ?? Array.Empty<AutomationTrackInfo>();
    }

    /// <inheritdoc />
    public async Task<AutomationCurve[]> GetCurvesAsync(string? trackId = null, string? parameterId = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string>();
      if (!string.IsNullOrEmpty(trackId))
        queryParams.Add($"track_id={Uri.EscapeDataString(trackId)}");
      if (!string.IsNullOrEmpty(parameterId))
        queryParams.Add($"parameter_id={Uri.EscapeDataString(parameterId)}");

      var url = "/api/automation";
      if (queryParams.Count > 0)
        url += "?" + string.Join("&", queryParams);

      var result = await _backend.SendRequestAsync<object, AutomationCurve[]>(
          url,
          null,
          HttpMethod.Get,
          cancellationToken);
      return result ?? Array.Empty<AutomationCurve>();
    }

    /// <inheritdoc />
    public Task<AutomationCurve?> CreateCurveAsync(AutomationCreateRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<AutomationCreateRequest, AutomationCurve>(
          "/api/automation",
          request,
          HttpMethod.Post,
          cancellationToken);
    }

    /// <inheritdoc />
    public Task<AutomationCurve?> UpdateCurveAsync(string curveId, AutomationUpdateRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<AutomationUpdateRequest, AutomationCurve>(
          $"/api/automation/{Uri.EscapeDataString(curveId)}",
          request,
          HttpMethod.Put,
          cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteCurveAsync(string curveId, CancellationToken cancellationToken = default)
    {
      await _backend.SendRequestAsync<object, object>(
          $"/api/automation/{Uri.EscapeDataString(curveId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);
    }

    /// <inheritdoc />
    public Task<AutomationTrackParametersResponse?> GetTrackParametersAsync(string trackId, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, AutomationTrackParametersResponse>(
          $"/api/automation/tracks/{Uri.EscapeDataString(trackId)}/parameters",
          null,
          HttpMethod.Get,
          cancellationToken);
    }
  }
}
