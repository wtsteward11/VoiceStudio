using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Core.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

public sealed class DialogueServiceClient : IDialogueServiceClient
{
  private readonly IBackendClient _backend;

  public DialogueServiceClient(IBackendClient backend)
  {
    _backend = backend ?? throw new ArgumentNullException(nameof(backend));
  }

  /// <inheritdoc />
  public async Task<RegenerateDialogueSegmentResponse> RegenerateSegmentAsync(
      string segmentId,
      RegenerateDialogueSegmentRequest request,
      CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(segmentId))
      throw new ArgumentException("Segment id is required.", nameof(segmentId));
    var ep = $"/api/dialogue/segments/{Uri.EscapeDataString(segmentId)}/regenerate";
    var resp = await _backend
        .SendRequestAsync<RegenerateDialogueSegmentRequest, RegenerateDialogueSegmentResponse>(
            ep,
            request,
            HttpMethod.Post,
            cancellationToken)
        .ConfigureAwait(false);
    if (resp == null)
      throw new InvalidOperationException("Dialogue regenerate returned an empty response.");
    return resp;
  }
}
