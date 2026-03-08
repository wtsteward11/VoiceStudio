using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App
{
    public sealed partial class MainWindow
    {
        internal async Task<(string[] Steps, bool TimedOut, string? TimedOutStep)> RunGateCUiSmokeNavigationAsync(string crashDir)
        {
            // Deterministic Gate C UI smoke: exercise primary nav buttons to surface binding failures.
            var executed = new List<string>();

            var perStepTimeout = TimeSpan.FromSeconds(12);
            var warmupTimeout = TimeSpan.FromSeconds(30);
            var stepsLogPath = System.IO.Path.Combine(crashDir, "ui_smoke_steps_latest.log");
            try
            {
                System.IO.Directory.CreateDirectory(crashDir);
                System.IO.File.WriteAllText(
                  stepsLogPath,
                  $"timestamp_utc\t{DateTime.UtcNow:o}{Environment.NewLine}",
                  System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MainWindow.Task");
            }

            void AppendStepLog(string line)
            {
                try
                {
                    System.IO.File.AppendAllText(
                      stepsLogPath,
                      $"{DateTime.UtcNow:o}\t{line}{Environment.NewLine}",
                      System.Text.Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MainWindow.AppendStepLog");
                }
            }

            var dispatcher = this.DispatcherQueue;
            Task RunOnUiThreadAsync(string stepName, Action action)
            {
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                try
                {
                    var enqueued = dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            AppendStepLog($"DISPATCH_ENTER\t{stepName}");
                            action();
                            AppendStepLog($"DISPATCH_EXIT\t{stepName}");
                            tcs.TrySetResult(null);
                        }
                        catch (Exception ex)
                        {
                            AppendStepLog($"DISPATCH_EXCEPTION\t{stepName}\t{ex.GetType().Name}\t{ex.Message}");
                            tcs.TrySetException(ex);
                        }
                    });

                    if (!enqueued)
                    {
                        AppendStepLog($"ENQUEUE_FAILED\t{stepName}");
                        tcs.TrySetException(new InvalidOperationException("Failed to enqueue UI smoke step onto DispatcherQueue."));
                    }
                }
                catch (Exception ex)
                {
                    AppendStepLog($"ENQUEUE_EXCEPTION\t{stepName}\t{ex.GetType().Name}\t{ex.Message}");
                    tcs.TrySetException(ex);
                }

                return tcs.Task;
            }

            Task RunOnUiThreadAsyncTask(string stepName, Func<Task> action)
            {
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                try
                {
                    var enqueued = dispatcher.TryEnqueue(() =>
                    {
                        async Task RunAsync()
                        {
                            try
                            {
                                AppendStepLog($"DISPATCH_ENTER\t{stepName}");
                                await action();
                                AppendStepLog($"DISPATCH_EXIT\t{stepName}");
                                tcs.TrySetResult(null);
                            }
                            catch (Exception ex)
                            {
                                AppendStepLog($"DISPATCH_EXCEPTION\t{stepName}\t{ex.GetType().Name}\t{ex.Message}");
                                tcs.TrySetException(ex);
                            }
                        }
                        _ = RunAsync();
                    });

                    if (!enqueued)
                    {
                        AppendStepLog($"ENQUEUE_FAILED\t{stepName}");
                        tcs.TrySetException(new InvalidOperationException("Failed to enqueue UI smoke step onto DispatcherQueue."));
                    }
                }
                catch (Exception ex)
                {
                    AppendStepLog($"ENQUEUE_EXCEPTION\t{stepName}\t{ex.GetType().Name}\t{ex.Message}");
                    tcs.TrySetException(ex);
                }

                return tcs.Task;
            }

            // Warm up: verify the UI thread is pumping the DispatcherQueue before we attempt navigation.
            AppendStepLog("WARMUP_BEGIN");
            var warmupTask = RunOnUiThreadAsync("Warmup", () => { });
            var warmupCompleted = await Task.WhenAny(warmupTask, Task.Delay(warmupTimeout)).ConfigureAwait(false);
            if (warmupCompleted != warmupTask)
            {
                AppendStepLog($"WARMUP_TIMEOUT\ttimeout_sec={(int)warmupTimeout.TotalSeconds}");
                return (executed.ToArray(), true, "Warmup");
            }

            try
            {
                await warmupTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppendStepLog($"WARMUP_EXCEPTION\t{ex.GetType().Name}\t{ex.Message}");
                throw;
            }

            AppendStepLog("WARMUP_DONE");

            async Task AssertPanelOpened(string panelId, PanelRegion region)
            {
                if (!await OpenPanelByIdAsync(panelId, region))
                    throw new InvalidOperationException($"Smoke: failed to open panel {panelId}");
            }

            var steps = new (string Name, Func<Task> Action)[]
            {
        // Primary navigation buttons (8 steps)
        ("NavStudio", () => ExecuteNavCommandAsync("nav.studio", "Timeline", PanelRegion.Center, "NavStudio")),
        ("NavProfiles", () => ExecuteNavCommandAsync("nav.profiles", "Profiles", PanelRegion.Left, "NavProfiles")),
        ("NavLibrary", () => ExecuteNavCommandAsync("nav.library", "Library", PanelRegion.Left, "NavLibrary")),
        ("NavTrain", () => ExecuteNavCommandAsync("nav.train", "Training", PanelRegion.Left, "NavTrain")),
        ("NavEffects", () => ExecuteNavCommandAsync("nav.effects", "EffectsMixer", PanelRegion.Right, "NavEffects")),
        ("NavAnalyze", () => ExecuteNavCommandAsync("nav.analyze", "Analyzer", PanelRegion.Right, "NavAnalyze")),
        ("NavSettings", () => ExecuteNavCommandAsync("nav.settings", "Settings", PanelRegion.Right, "NavSettings")),
        ("NavLogs", () => ExecuteNavCommandAsync("nav.logs", "Diagnostics", PanelRegion.Bottom, "NavLogs")),

        // Core synthesis panels (4 steps)
        ("PanelVoiceSynthesis", () => AssertPanelOpened("VoiceSynthesis", PanelRegion.Center)),
        ("PanelEnsembleSynthesis", () => AssertPanelOpened("EnsembleSynthesis", PanelRegion.Center)),
        ("PanelBatchProcessing", () => AssertPanelOpened("BatchProcessing", PanelRegion.Center)),
        ("PanelTextSpeechEditor", () => AssertPanelOpened("TextSpeechEditor", PanelRegion.Center)),

        // Training panels (3 steps)
        ("PanelTrainingDatasetEditor", () => AssertPanelOpened("TrainingDatasetEditor", PanelRegion.Center)),
        ("PanelModelManager", () => AssertPanelOpened("ModelManager", PanelRegion.Center)),
        ("PanelTraining", () => AssertPanelOpened("Training", PanelRegion.Center)),

        // Audio processing panels (4 steps)
        ("PanelTranscribe", () => AssertPanelOpened("Transcribe", PanelRegion.Center)),
        ("PanelRecording", () => AssertPanelOpened("Recording", PanelRegion.Right)),
        ("PanelAudioAnalysis", () => AssertPanelOpened("AudioAnalysis", PanelRegion.Center)),
        ("PanelQualityControl", () => AssertPanelOpened("QualityControl", PanelRegion.Right)),

        // Utility panels (3 steps)
        ("PanelTimeline", () => AssertPanelOpened("Timeline", PanelRegion.Center)),
        ("PanelDiagnostics", () => AssertPanelOpened("Diagnostics", PanelRegion.Right)),
        ("PanelHelp", () => AssertPanelOpened("Help", PanelRegion.Right)),

        // Voice control panels (3 steps)
        ("PanelVoiceMorph", () => AssertPanelOpened("VoiceMorph", PanelRegion.Center)),
        ("PanelProsody", () => AssertPanelOpened("Prosody", PanelRegion.Right)),
        ("PanelEmotionControl", () => AssertPanelOpened("EmotionControl", PanelRegion.Right)),
            };

            foreach (var step in steps)
            {
                executed.Add(step.Name);
                AppendStepLog($"STEP_BEGIN\t{step.Name}");

                var stepTask = RunOnUiThreadAsyncTask(step.Name, step.Action);
                var completed = await Task.WhenAny(stepTask, Task.Delay(perStepTimeout)).ConfigureAwait(false);
                if (completed != stepTask)
                {
                    AppendStepLog($"STEP_TIMEOUT\t{step.Name}\ttimeout_sec={(int)perStepTimeout.TotalSeconds}");
                    return (executed.ToArray(), true, step.Name);
                }

                try
                {
                    await stepTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppendStepLog($"STEP_EXCEPTION\t{step.Name}\t{ex.GetType().Name}\t{ex.Message}");
                    throw;
                }

                AppendStepLog($"STEP_DONE\t{step.Name}");
                await Task.Delay(250).ConfigureAwait(false);
            }

            // ── TD-036: Workspace profile switch smoke steps ──
            // Switch to "training" workspace and assert center panel matches the Training layout.
            // This validates that workspace switching, embedded layout loading, and panel restoration work end-to-end.
            var workspaceSteps = new (string Name, string ProfileId, string ExpectedCenterViewType)[]
            {
        ("WorkspaceSwitchToTraining", "training", "TrainingView"),
        ("WorkspaceSwitchToStudio", "studio", "TimelineView"),
            };

            foreach (var wsStep in workspaceSteps)
            {
                executed.Add(wsStep.Name);
                AppendStepLog($"STEP_BEGIN\t{wsStep.Name}");

                try
                {
                    // Perform the workspace switch on a background thread (the async service call),
                    // then dispatch the layout restoration and assertion onto the UI thread.
                    if (_panelStateService != null)
                    {
                        var switchResult = await _panelStateService.SwitchWorkspaceProfileAsync(wsStep.ProfileId).ConfigureAwait(false);
                        AppendStepLog($"WORKSPACE_SWITCH_RESULT\t{wsStep.Name}\tprofile={wsStep.ProfileId}\tsuccess={switchResult}");

                        // Allow UI to process the WorkspaceProfileChanged event and apply layout
                        await Task.Delay(1200).ConfigureAwait(false);

                        // Assert on the UI thread: verify center panel content type matches expected
                        var assertTask = RunOnUiThreadAsync($"Assert_{wsStep.Name}", () =>
                        {
                            var centerPanelHost = FindNameOnContent("CenterPanelHost") as Controls.PanelHost;
                            var actualContentType = centerPanelHost?.Content?.GetType().Name ?? "(null)";
                            AppendStepLog($"WORKSPACE_ASSERT\t{wsStep.Name}\texpected={wsStep.ExpectedCenterViewType}\tactual={actualContentType}");

                            if (!string.Equals(actualContentType, wsStep.ExpectedCenterViewType, StringComparison.Ordinal))
                            {
                                var message = $"Workspace restore failed: expected '{wsStep.ExpectedCenterViewType}', got '{actualContentType}'";
                                AppendStepLog($"WORKSPACE_ASSERT_FAIL\t{wsStep.Name}\t{message}");
                                throw new InvalidOperationException(message);
                            }
                        });

                        var assertCompleted = await Task.WhenAny(assertTask, Task.Delay(perStepTimeout)).ConfigureAwait(false);
                        if (assertCompleted != assertTask)
                        {
                            AppendStepLog($"STEP_TIMEOUT\tAssert_{wsStep.Name}\ttimeout_sec={(int)perStepTimeout.TotalSeconds}");
                            return (executed.ToArray(), true, $"Assert_{wsStep.Name}");
                        }

                        await assertTask.ConfigureAwait(false);
                    }
                    else
                    {
                        AppendStepLog($"WORKSPACE_SKIP\t{wsStep.Name}\tPanelStateService not available");
                    }
                }
                catch (Exception ex)
                {
                    AppendStepLog($"STEP_EXCEPTION\t{wsStep.Name}\t{ex.GetType().Name}\t{ex.Message}");
                    throw;
                }

                AppendStepLog($"STEP_DONE\t{wsStep.Name}");
                await Task.Delay(250).ConfigureAwait(false);
            }

            return (executed.ToArray(), false, null);
        }
    }
}
