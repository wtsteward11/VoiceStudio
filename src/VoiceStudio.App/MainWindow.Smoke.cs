using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App
{
    public sealed partial class MainWindow
    {
        internal async Task<(string[] Steps, bool TimedOut, string? TimedOutStep, bool SynthesisStepRan, bool PlaybackInvoked, string? AudioId, bool StreamCheckPassed, bool TempFileCreated, bool PlaybackStarted, double PlaybackPositionAdvancedMs, bool LibraryTempFileCreated, bool LibraryPlaybackStarted, double LibraryPlaybackPositionAdvancedMs, List<(string Step, string Error)> Failures)> RunGateCUiSmokeNavigationAsync(string crashDir)
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

            Task YieldToDispatcherAsync()
            {
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (dispatcher.TryEnqueue(() => tcs.TrySetResult(null)))
                    return tcs.Task;
                tcs.TrySetResult(null);
                return tcs.Task;
            }

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

            Task<T> GetFromUiThreadAsync<T>(string stepName, Func<T> func)
            {
                var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    var enqueued = dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            tcs.TrySetResult(func());
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    });
                    if (!enqueued)
                        tcs.TrySetException(new InvalidOperationException("Failed to enqueue onto DispatcherQueue."));
                }
                catch (Exception ex)
                {
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
                return (executed.ToArray(), true, "Warmup", false, false, null, false, false, false, 0.0, false, false, 0.0, new List<(string Step, string Error)>());
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
            var audioId = (string?)null;
            var streamCheckPassed = false;
            var tempFileCreated = false;
            var playbackStarted = false;
            var playbackPositionAdvancedMs = 0.0;
            var libraryTempFileCreated = false;
            var libraryPlaybackStarted = false;
            var libraryPlaybackPositionAdvancedMs = 0.0;
            var synthesisFailures = new List<(string Step, string Error)>();
            var synthProof = new SynthesisProof();
            var libProof = new LibraryProof();

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
        ("SynthesisAndPlayback", () => RunSynthesisAndPlaybackAsync(AppendStepLog, synthProof)),
        ("LibraryPlayback", () => RunLibraryPlaybackAsync(AppendStepLog, synthProof.AudioId ?? "", libProof)),
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
                var synthTimeout = step.Name == "SynthesisAndPlayback" ? TimeSpan.FromSeconds(45)
                    : step.Name == "LibraryPlayback" ? TimeSpan.FromSeconds(30)
                    : perStepTimeout;
                var completed = await Task.WhenAny(stepTask, Task.Delay(synthTimeout)).ConfigureAwait(false);
                if (completed != stepTask)
                {
                    AppendStepLog($"STEP_TIMEOUT\t{step.Name}\ttimeout_sec={(int)synthTimeout.TotalSeconds}");
                    return (executed.ToArray(), true, step.Name, synthesisStepRan, playbackInvoked, audioId, streamCheckPassed, tempFileCreated, playbackStarted, playbackPositionAdvancedMs, libraryTempFileCreated, libraryPlaybackStarted, libraryPlaybackPositionAdvancedMs, synthesisFailures);
                }

                try
                {
                    await stepTask.ConfigureAwait(false);
                    if (step.Name == "SynthesisAndPlayback")
                    {
                        synthesisStepRan = true;
                        playbackInvoked = true;
                        audioId = synthProof.AudioId;
                        streamCheckPassed = synthProof.StreamCheckPassed;
                        playbackPositionAdvancedMs = synthProof.PlaybackPositionAdvancedMs;
                        tempFileCreated = synthProof.TempFileCreated;
                        playbackStarted = synthProof.PlaybackStarted;
                    }
                    if (step.Name == "LibraryPlayback")
                    {
                        libraryTempFileCreated = libProof.TempFileCreated;
                        libraryPlaybackStarted = libProof.PlaybackStarted;
                        libraryPlaybackPositionAdvancedMs = libProof.PlaybackPositionAdvancedMs;
                    }
                }
                catch (Exception ex)
                {
                    AppendStepLog($"STEP_EXCEPTION\t{step.Name}\t{ex.GetType().Name}\t{ex.Message}");
                    if (step.Name == "SynthesisAndPlayback")
                    {
                        synthesisFailures.Add((step.Name, $"{ex.GetType().Name}: {ex.Message}"));
                        return (executed.ToArray(), false, null, false, false, null, false, false, false, 0.0, false, false, 0.0, synthesisFailures);
                    }
                    throw;
                }

                AppendStepLog($"STEP_DONE\t{step.Name}");
                await YieldToDispatcherAsync();
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

                        // Wait until center panel type changes to expected or timeout (5s)
                        var layoutDeadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < layoutDeadline)
                        {
                            var actualContentType = await GetFromUiThreadAsync<string>($"GetCenterType_{wsStep.Name}", () =>
                            {
                                var centerPanelHost = FindNameOnContent("CenterPanelHost") as Controls.PanelHost;
                                return centerPanelHost?.Content?.GetType().Name ?? "(null)";
                            }).ConfigureAwait(false);
                            if (string.Equals(actualContentType, wsStep.ExpectedCenterViewType, StringComparison.Ordinal))
                                break;
                            await Task.Delay(100).ConfigureAwait(false);
                        }

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
                            return (executed.ToArray(), true, $"Assert_{wsStep.Name}", synthesisStepRan, playbackInvoked, audioId, streamCheckPassed, tempFileCreated, playbackStarted, playbackPositionAdvancedMs, libraryTempFileCreated, libraryPlaybackStarted, libraryPlaybackPositionAdvancedMs, synthesisFailures);
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
                await YieldToDispatcherAsync();
            }

            try
            {
                var metrics = AppServices.TryGetRequestMetricsService();
                if (metrics != null)
                {
                    var counts = metrics.GetCountsPerMinute();
                    var json = System.Text.Json.JsonSerializer.Serialize(counts);
                    AppendStepLog($"REQUEST_COUNTS\t{json}");
                }
            }
            catch (Exception ex)
            {
                AppendStepLog($"REQUEST_COUNTS_ERROR\t{ex.Message}");
            }

            return (executed.ToArray(), false, null, synthesisStepRan, playbackInvoked, audioId, streamCheckPassed, tempFileCreated, playbackStarted, playbackPositionAdvancedMs, libraryTempFileCreated, libraryPlaybackStarted, libraryPlaybackPositionAdvancedMs, synthesisFailures);
        }

        private sealed class SynthesisProof
        {
            public string? AudioId;
            public bool StreamCheckPassed;
            public bool TempFileCreated;
            public bool PlaybackStarted;
            public double PlaybackPositionAdvancedMs;
        }

        private sealed class LibraryProof
        {
            public bool TempFileCreated;
            public bool PlaybackStarted;
            public double PlaybackPositionAdvancedMs;
        }

        private async Task RunSynthesisAndPlaybackAsync(Action<string> appendStepLog, SynthesisProof proof)
        {
            var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/') ?? "http://localhost:8000";
            var dispatcher = this.DispatcherQueue;

            Task RunOnUiAsync(Func<Task> action)
            {
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        await action();
                        tcs.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
                return tcs.Task;
            }

            Task<T> RunOnUiAsyncWithResult<T>(Func<Task<T>> action)
            {
                var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        tcs.TrySetResult(await action());
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
                return tcs.Task;
            }

            appendStepLog("BACKEND_POLL_BEGIN");
            await Task.Run(async () =>
            {
                var backendDeadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < backendDeadline)
                {
                    if (await BackendClient.TryCheckHealthAsync(baseUrl, 2000))
                        return;
                    await Task.Delay(500);
                }
                if (!await BackendClient.TryCheckHealthAsync(baseUrl, 2000))
                {
                    appendStepLog("BACKEND_POLL_FAIL\tBackend not healthy within 10s");
                    throw new InvalidOperationException("Backend not healthy within 10s");
                }
            });
            appendStepLog("BACKEND_POLL_PASS");

            var proofAudioId = await RunOnUiAsyncWithResult<string>(async () =>
            {
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
                await vm.LoadProfilesCommand.ExecuteAsync(null);
            }
            appendStepLog("PROFILES_POLL_BEGIN");
            var profilesDeadline = DateTime.UtcNow.AddSeconds(8);
            while (vm.Profiles.Count == 0 && DateTime.UtcNow < profilesDeadline)
            {
                await Task.Delay(200);
            }
            if (vm.Profiles.Count == 0)
            {
                appendStepLog("PROFILES_POLL_FAIL\tNo profiles loaded within 8s");
                throw new InvalidOperationException("No profiles loaded within 8s");
            }
            appendStepLog("PROFILES_POLL_PASS");

            vm.SelectedProfile = vm.Profiles[0];
            vm.Text = "Daily-driver smoke test.";

            if (!vm.SynthesizeCommand.CanExecute(null))
            {
                appendStepLog("SYNTHESIS_SKIP\tSynthesizeCommand not executable");
                throw new InvalidOperationException("SynthesizeCommand not executable");
            }

            appendStepLog("SYNTHESIS_RUN\tExecuting synthesize");
            await vm.SynthesizeCommand.ExecuteAsync(null);

            var waitStart = DateTime.UtcNow;
            while (vm.WorkflowState != SynthesisWorkflowState.AudioReady && (DateTime.UtcNow - waitStart).TotalSeconds < 15)
            {
                await Task.Delay(200);
            }

            if (vm.WorkflowState != SynthesisWorkflowState.AudioReady)
            {
                appendStepLog($"SYNTHESIS_FAIL\tWorkflowState={vm.WorkflowState}");
                throw new InvalidOperationException($"Synthesis did not reach AudioReady (state={vm.WorkflowState})");
            }

            var audioId = vm.LastSynthesizedAudioId;
            if (string.IsNullOrWhiteSpace(audioId))
            {
                appendStepLog("STREAM_CHECK_FAIL\tLastSynthesizedAudioId is empty");
                throw new InvalidOperationException("Stream check failed: LastSynthesizedAudioId is empty");
            }
            return audioId;
            });

            var streamCheckPassed = await Task.Run(async () =>
            {
                using var httpClient = new HttpClient();
                var streamUrl = $"{baseUrl}/api/audio/file/{Uri.EscapeDataString(proofAudioId)}";
                var resp = await httpClient.GetAsync(streamUrl);
                if (!resp.IsSuccessStatusCode)
                {
                    appendStepLog($"STREAM_CHECK_FAIL\tGET {streamUrl} returned {resp.StatusCode}");
                    throw new InvalidOperationException($"Stream check failed: GET /api/audio/file/id returned {resp.StatusCode}");
                }
                await using var stream = await resp.Content.ReadAsStreamAsync();
                var header = new byte[4];
                var read = await stream.ReadAsync(header.AsMemory(0, 4));
                if (read < 4 || header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F')
                {
                    appendStepLog("STREAM_CHECK_FAIL\tResponse is not RIFF format");
                    throw new InvalidOperationException("Stream check failed: response is not RIFF format");
                }
                return true;
            });

            proof.AudioId = proofAudioId;
            proof.StreamCheckPassed = streamCheckPassed;
            appendStepLog("STREAM_CHECK_PASS");

            await RunOnUiAsync(async () =>
            {
                appendStepLog("PLAYBACK_RUN\tExecuting play");
                var centerHost = FindNameOnContent("CenterPanelHost") as PanelHost;
                if (centerHost?.Content is not VoiceSynthesisView synthView)
                {
                    appendStepLog("PLAYBACK_SKIP\tVoiceSynthesisView not in center panel");
                    throw new InvalidOperationException("VoiceSynthesisView not in center panel");
                }
                var vm = synthView.ViewModel;
                if (!vm.PlayAudioCommand.CanExecute(null))
                {
                    appendStepLog("PLAYBACK_SKIP\tPlayAudioCommand not executable");
                    throw new InvalidOperationException("PlayAudioCommand not executable");
                }

                _ = vm.PlayAudioCommand.ExecuteAsync(null);
                var audioPlayer = AppServices.GetAudioPlayerService();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(100);
                    if (audioPlayer.IsPlaying)
                    {
                        proof.PlaybackStarted = true;
                    }
                    var tempPath = audioPlayer.LastTempPlaybackPath;
                    if (tempPath != null && File.Exists(tempPath))
                    {
                        var fi = new FileInfo(tempPath);
                        if (fi.Length > 100)
                        {
                            proof.TempFileCreated = true;
                        }
                    }
                    if (proof.PlaybackStarted && proof.TempFileCreated)
                    {
                        break;
                    }
                }

                if (!proof.TempFileCreated || !proof.PlaybackStarted)
                {
                    appendStepLog("PLAYBACK_FAIL\tPlayback did not start within 5s");
                    throw new InvalidOperationException("Playback did not start within 5s");
                }

                var pos1 = audioPlayer.Position;
                await Task.Delay(500);
                var pos2 = audioPlayer.Position;
                var advancedMs = (pos2 - pos1) * 1000.0;
                proof.PlaybackPositionAdvancedMs = advancedMs;
                if (advancedMs < 250)
                {
                    appendStepLog($"PLAYBACK_POSITION_FAIL\tadvancement={advancedMs:F0}ms (required >= 250ms)");
                    throw new InvalidOperationException($"Playback position did not advance (got {advancedMs:F0}ms, required >= 250ms)");
                }
                appendStepLog($"PLAYBACK_POSITION_PASS\tadvancement={advancedMs:F0}ms");
                appendStepLog("PLAYBACK_DONE");
            });
        }

        private async Task RunLibraryPlaybackAsync(Action<string> appendStepLog, string audioId, LibraryProof proof)
        {
            if (string.IsNullOrWhiteSpace(audioId))
            {
                appendStepLog("LIBRARY_PLAYBACK_SKIP\tNo audio_id from synthesis");
                throw new InvalidOperationException("Library playback requires audio_id from synthesis");
            }

            appendStepLog("LIBRARY_NAV_BEGIN");
            if (!await OpenPanelByIdAsync("Library", PanelRegion.Left))
            {
                appendStepLog("LIBRARY_NAV_FAIL\tFailed to open Library panel");
                throw new InvalidOperationException("Smoke: failed to open Library panel");
            }
            appendStepLog("LIBRARY_NAV_PASS");

            await Task.Delay(300);

            var leftHost = FindNameOnContent("LeftPanelHost") as PanelHost;
            if (leftHost?.Content is not LibraryView libraryView)
            {
                appendStepLog("LIBRARY_PLAYBACK_SKIP\tLibraryView not in left panel");
                throw new InvalidOperationException("LibraryView not found in left panel");
            }

            var vm = libraryView.ViewModel;
            appendStepLog("LIBRARY_REFRESH_BEGIN");
            if (vm.RefreshCommand.CanExecute(null))
            {
                await vm.RefreshCommand.ExecuteAsync(null);
            }
            var refreshDeadline = DateTime.UtcNow.AddSeconds(8);
            while (vm.Assets.Count == 0 && DateTime.UtcNow < refreshDeadline)
            {
                await Task.Delay(200);
            }
            if (vm.Assets.Count == 0)
            {
                appendStepLog("LIBRARY_REFRESH_FAIL\tNo assets in Library within 8s");
                throw new InvalidOperationException("Library has no assets within 8s");
            }
            appendStepLog("LIBRARY_REFRESH_PASS");

            var asset = vm.Assets.FirstOrDefault(a => string.Equals(a.Id, audioId, StringComparison.Ordinal))
                ?? vm.Assets.FirstOrDefault(a => IsPlayableLibraryAsset(a));
            if (asset == null)
            {
                appendStepLog($"LIBRARY_PLAYBACK_SKIP\tNo playable asset (audio_id={audioId})");
                throw new InvalidOperationException($"No playable asset in Library for audio_id={audioId}");
            }
            appendStepLog($"LIBRARY_PLAY_ASSET\tid={asset.Id}");

            if (!vm.PlayAssetCommand.CanExecute(asset))
            {
                appendStepLog("LIBRARY_PLAYBACK_SKIP\tPlayAssetCommand not executable");
                throw new InvalidOperationException("PlayAssetCommand not executable for asset");
            }

            vm.PlayAssetCommand.Execute(asset);
            var audioPlayer = AppServices.GetAudioPlayerService();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
                if (audioPlayer.IsPlaying)
                    proof.PlaybackStarted = true;
                var tempPath = audioPlayer.LastTempPlaybackPath;
                if (tempPath != null && File.Exists(tempPath))
                {
                    var fi = new FileInfo(tempPath);
                    if (fi.Length > 100)
                        proof.TempFileCreated = true;
                }
                if (proof.PlaybackStarted && proof.TempFileCreated)
                    break;
            }

            if (!proof.TempFileCreated || !proof.PlaybackStarted)
            {
                appendStepLog("LIBRARY_PLAYBACK_FAIL\tPlayback did not start within 5s");
                throw new InvalidOperationException("Library playback did not start within 5s");
            }

            var pos1 = audioPlayer.Position;
            await Task.Delay(500);
            var pos2 = audioPlayer.Position;
            var advancedMs = (pos2 - pos1) * 1000.0;
            proof.PlaybackPositionAdvancedMs = advancedMs;
            if (advancedMs < 250)
            {
                appendStepLog($"LIBRARY_POSITION_FAIL\tadvancement={advancedMs:F0}ms (required >= 250ms)");
                throw new InvalidOperationException($"Library playback position did not advance (got {advancedMs:F0}ms, required >= 250ms)");
            }
            appendStepLog($"LIBRARY_POSITION_PASS\tadvancement={advancedMs:F0}ms");
            appendStepLog("LIBRARY_PLAYBACK_DONE");
        }

        private static bool IsPlayableLibraryAsset(LibraryAsset asset)
        {
            var audioTypes = new[] { "audio", "wav", "mp3", "flac", "ogg", "m4a", "recording" };
            var voiceTypes = new[] { "voice", "voice_profile", "profile", "clone", "xtts", "rvc" };
            var t = asset.Type?.ToLowerInvariant() ?? "";
            return audioTypes.Contains(t) || voiceTypes.Contains(t)
                || asset.Path?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true
                || asset.Path?.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) == true
                || asset.Path?.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
