using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App
{
    public sealed partial class MainWindow
    {
        internal async Task<(string[] Steps, bool TimedOut, string? TimedOutStep, bool SynthesisStepRan, bool PlaybackInvoked, List<(string Step, string Error)> Failures)> RunGateCUiSmokeNavigationAsync(string crashDir)
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
                return (executed.ToArray(), true, "Warmup", false, false, new List<(string, string)>());
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

            var synthesisStepRan = false;
            var playbackInvoked = false;
            var synthesisFailures = new List<(string Step, string Error)>();

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
        ("SynthesisAndPlayback", () => RunSynthesisAndPlaybackAsync(AppendStepLog)),
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
                    return (executed.ToArray(), true, step.Name, synthesisStepRan, playbackInvoked, synthesisFailures);
                }

                try
                {
                    await stepTask.ConfigureAwait(false);
                    if (step.Name == "SynthesisAndPlayback")
                    {
                        synthesisStepRan = true;
                        playbackInvoked = true;
                    }
                }
                catch (Exception ex)
                {
                    AppendStepLog($"STEP_EXCEPTION\t{step.Name}\t{ex.GetType().Name}\t{ex.Message}");
                    if (step.Name == "SynthesisAndPlayback")
                    {
                        synthesisFailures.Add((step.Name, $"{ex.GetType().Name}: {ex.Message}"));
                        return (executed.ToArray(), false, null, false, false, synthesisFailures);
                    }
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
                            return (executed.ToArray(), true, $"Assert_{wsStep.Name}", synthesisStepRan, playbackInvoked, synthesisFailures);
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

            return (executed.ToArray(), false, null, synthesisStepRan, playbackInvoked, synthesisFailures);
        }

        private async Task RunSynthesisAndPlaybackAsync(Action<string> appendStepLog)
        {
            // Allow backend to be ready (app starts it on launch)
            await Task.Delay(2000).ConfigureAwait(false);

            var centerHost = FindNameOnContent("CenterPanelHost") as PanelHost;
            if (centerHost?.Content is not VoiceSynthesisView synthView)
            {
                appendStepLog("SYNTHESIS_SKIP\tVoiceSynthesisView not in center panel");
                throw new InvalidOperationException("VoiceSynthesisView not in center panel");
            }

            var vm = synthView.ViewModel;
            appendStepLog("SYNTHESIS_BEGIN\tLoading profiles");

            if (vm.LoadProfilesCommand.CanExecute(null))
            {
                await vm.LoadProfilesCommand.ExecuteAsync(null).ConfigureAwait(false);
            }
            await Task.Delay(1500).ConfigureAwait(false);

            if (vm.Profiles.Count == 0)
            {
                appendStepLog("SYNTHESIS_SKIP\tNo profiles loaded (backend may not be ready)");
                throw new InvalidOperationException("No profiles loaded - backend may not be ready");
            }

            vm.SelectedProfile = vm.Profiles[0];
            vm.Text = "Daily-driver smoke test.";
            await Task.Delay(300).ConfigureAwait(false);

            if (!vm.SynthesizeCommand.CanExecute(null))
            {
                appendStepLog("SYNTHESIS_SKIP\tSynthesizeCommand not executable");
                throw new InvalidOperationException("SynthesizeCommand not executable");
            }

            appendStepLog("SYNTHESIS_RUN\tExecuting synthesize");
            await vm.SynthesizeCommand.ExecuteAsync(null).ConfigureAwait(false);

            var waitStart = DateTime.UtcNow;
            while (vm.WorkflowState != SynthesisWorkflowState.AudioReady && (DateTime.UtcNow - waitStart).TotalSeconds < 15)
            {
                await Task.Delay(200).ConfigureAwait(false);
            }

            if (vm.WorkflowState != SynthesisWorkflowState.AudioReady)
            {
                appendStepLog($"SYNTHESIS_FAIL\tWorkflowState={vm.WorkflowState}");
                throw new InvalidOperationException($"Synthesis did not reach AudioReady (state={vm.WorkflowState})");
            }

            appendStepLog("PLAYBACK_RUN\tExecuting play");
            if (vm.PlayAudioCommand.CanExecute(null))
            {
                await vm.PlayAudioCommand.ExecuteAsync(null).ConfigureAwait(false);
            }
            appendStepLog("PLAYBACK_DONE");
        }
    }
}
