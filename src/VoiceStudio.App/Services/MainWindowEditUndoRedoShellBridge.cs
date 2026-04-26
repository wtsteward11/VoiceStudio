// VoiceStudio — GAP-008 Slice 29: MainWindow Edit menu — Undo / Redo (bounded).

using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Edit → Undo/Redo: delegates to <see cref="UndoRedoService"/>; logs failures.
/// </summary>
public sealed class MainWindowEditUndoRedoShellBridge
{
    public void ExecuteUndo(Func<UndoRedoService> getUndoRedoService, Action<Exception, string> logError)
    {
        ArgumentNullException.ThrowIfNull(getUndoRedoService);
        ArgumentNullException.ThrowIfNull(logError);

        try
        {
            var undoService = getUndoRedoService();
            if (undoService.CanUndo)
            {
                undoService.Undo();
            }
        }
        catch (Exception ex)
        {
            logError(ex, "ExecuteUndo");
        }
    }

    public void ExecuteRedo(Func<UndoRedoService> getUndoRedoService, Action<Exception, string> logError)
    {
        ArgumentNullException.ThrowIfNull(getUndoRedoService);
        ArgumentNullException.ThrowIfNull(logError);

        try
        {
            var undoService = getUndoRedoService();
            if (undoService.CanRedo)
            {
                undoService.Redo();
            }
        }
        catch (Exception ex)
        {
            logError(ex, "ExecuteRedo");
        }
    }
}
