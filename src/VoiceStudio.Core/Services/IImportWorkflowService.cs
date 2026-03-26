// VoiceStudio - Import workflow abstraction (Transport Coherence Wave 4 Phase 2)

using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services;

/// <summary>
/// Orchestrates the import-audio workflow: file picker, upload, event publish, context update, toast.
/// Shell triggers import; this service owns the workflow.
/// </summary>
public interface IImportWorkflowService
{
    /// <summary>
    /// Runs the import workflow: pick file, upload to library, publish AssetAddedEvent,
    /// set current playable, show toast. Returns true if a file was imported.
    /// </summary>
    /// <param name="parentWindowHandle">Window handle for file picker parent.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> ImportAudioFileAsync(IntPtr parentWindowHandle, CancellationToken ct = default);
}
