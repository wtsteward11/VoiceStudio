using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Core.Models;

namespace VoiceStudio.Core.Services
{
  public interface IBackendClient
  {
    /// <summary>
    /// Gets the WebSocket service for real-time updates.
    /// </summary>
    IWebSocketService? WebSocketService { get; }

    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    bool IsConnected { get; }

    Task<TResponse> SendRequestAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default);
    Task<TResponse?> SendRequestAsync<TRequest, TResponse>(string endpoint, TRequest? request, System.Net.Http.HttpMethod httpMethod, CancellationToken cancellationToken = default) where TResponse : class;
    Task<TResponse> SendMcpOperationAsync<TRequest, TResponse>(string operation, TRequest payload, CancellationToken cancellationToken = default);

    // Health check
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
    Task<Telemetry> GetTelemetryAsync(CancellationToken cancellationToken = default);

    // API Version validation
    /// <summary>
    /// Checks API version compatibility with the backend.
    /// </summary>
    Task<VoiceStudio.App.Services.ApiVersionCheckResult> CheckApiVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets version information from the backend.
    /// </summary>
    Task<VoiceStudio.App.Services.ApiVersionInfo?> GetApiVersionInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates API version on startup and logs warnings if there are compatibility issues.
    /// </summary>
    Task<bool> ValidateApiVersionOnStartupAsync(CancellationToken cancellationToken = default);

    // Voice cloning endpoints
    Task<VoiceSynthesisResponse> SynthesizeVoiceAsync(VoiceSynthesisRequest request, CancellationToken cancellationToken = default);
    Task<VoiceAnalysisResponse> AnalyzeVoiceAsync(Stream audioFile, string? metrics = null, CancellationToken cancellationToken = default);
    Task<VoiceCloneResponse> CloneVoiceAsync(Stream referenceAudio, VoiceCloneRequest request, CancellationToken cancellationToken = default);

    // Profile management
    Task<List<VoiceProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);
    Task<VoiceProfile> GetProfileAsync(string profileId, CancellationToken cancellationToken = default);
    Task<VoiceProfile> CreateProfileAsync(string name, string language = "en", string? emotion = null, List<string>? tags = null, CancellationToken cancellationToken = default);
    Task<VoiceProfile> UpdateProfileAsync(string profileId, string? name = null, string? language = null, string? emotion = null, List<string>? tags = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);

    // Project management
    Task<List<Project>> GetProjectsAsync(CancellationToken cancellationToken = default);
    Task<Project> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);
    Task<Project> CreateProjectAsync(string name, string? description = null, CancellationToken cancellationToken = default);
    Task<Project> UpdateProjectAsync(string projectId, string? name = null, string? description = null, List<string>? voiceProfileIds = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default);

    // Project audio persistence
    Task<ProjectAudioFile> SaveAudioToProjectAsync(string projectId, string audioId, string? filename = null, CancellationToken cancellationToken = default);
    Task<List<ProjectAudioFile>> ListProjectAudioAsync(string projectId, CancellationToken cancellationToken = default);
    Task<Stream> GetProjectAudioAsync(string projectId, string filename, CancellationToken cancellationToken = default);

    // Audio retrieval
    Task<Stream> GetAudioStreamAsync(string audioId, CancellationToken cancellationToken = default);

    // Audio export/conversion
    /// <summary>
    /// Exports an audio file to the specified format.
    /// </summary>
    /// <param name="source">Source audio ID or filename.</param>
    /// <param name="targetFormat">Target format extension (e.g., "mp3", "flac").</param>
    /// <param name="sampleRate">Optional target sample rate in Hz.</param>
    /// <param name="channels">Optional number of channels.</param>
    /// <param name="bitrateKbps">Optional bitrate for lossy formats.</param>
    /// <param name="normalize">Whether to normalize audio.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream containing the converted audio file.</returns>
    Task<Stream> ExportAudioAsync(
        string source,
        string targetFormat,
        int? sampleRate = null,
        int? channels = null,
        int? bitrateKbps = null,
        bool normalize = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of supported audio formats for import/export.
    /// </summary>
    Task<List<VoiceStudio.App.Core.Models.AudioFormatInfo>> GetSupportedAudioFormatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads an audio file to the backend for analysis.
    /// </summary>
    /// <param name="filePath">Path to the local audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Upload response containing the audio ID.</returns>
    Task<AudioUploadResponse> UploadAudioFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file to a specified endpoint with progress reporting.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="endpoint">The API endpoint.</param>
    /// <param name="filePath">Path to the local file.</param>
    /// <param name="fileFieldName">Name of the form field for the file.</param>
    /// <param name="additionalData">Additional form data to include.</param>
    /// <param name="progress">Progress reporter (0-100).</param>
    /// <param name="timeout">Timeout for the upload operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the API.</returns>
    Task<TResponse?> UploadFileWithProgressAsync<TResponse>(
        string endpoint,
        string filePath,
        string fileFieldName = "file",
        Dictionary<string, string>? additionalData = null,
        IProgress<double>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where TResponse : class;

    /// <summary>
    /// Uploads multiple files to a specified endpoint with progress reporting.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="endpoint">The API endpoint.</param>
    /// <param name="files">Dictionary of field names to file paths.</param>
    /// <param name="additionalData">Additional form data to include.</param>
    /// <param name="progress">Progress reporter (0-100).</param>
    /// <param name="timeout">Timeout for the upload operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the API.</returns>
    Task<TResponse?> UploadFilesWithProgressAsync<TResponse>(
        string endpoint,
        Dictionary<string, string> files,
        Dictionary<string, string>? additionalData = null,
        IProgress<double>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where TResponse : class;

    // Audio visualization data
    Task<WaveformData> GetWaveformDataAsync(string audioId, int width = 1024, string mode = "peak", CancellationToken cancellationToken = default);
    Task<SpectrogramData> GetSpectrogramDataAsync(string audioId, int width = 512, int height = 256, CancellationToken cancellationToken = default);
    Task<AudioMeters> GetAudioMetersAsync(string audioId, CancellationToken cancellationToken = default);
    Task<RadarData> GetRadarDataAsync(string audioId, CancellationToken cancellationToken = default);
    Task<LoudnessData> GetLoudnessDataAsync(string audioId, double windowSize = 0.4, CancellationToken cancellationToken = default);
    Task<PhaseData> GetPhaseDataAsync(string audioId, double windowSize = 0.1, CancellationToken cancellationToken = default);

    // Timeline tracks and clips management
    Task<List<AudioTrack>> GetTracksAsync(string projectId, CancellationToken cancellationToken = default);
    Task<AudioTrack> GetTrackAsync(string projectId, string trackId, CancellationToken cancellationToken = default);
    Task<AudioTrack> CreateTrackAsync(string projectId, string name, string? engine = null, CancellationToken cancellationToken = default);
    Task<AudioTrack> UpdateTrackAsync(string projectId, string trackId, string? name = null, string? engine = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteTrackAsync(string projectId, string trackId, CancellationToken cancellationToken = default);

    Task<AudioClip> CreateClipAsync(string projectId, string trackId, AudioClip clip, CancellationToken cancellationToken = default);
    Task<AudioClip> UpdateClipAsync(string projectId, string trackId, string clipId, string? name = null, double? startTime = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteClipAsync(string projectId, string trackId, string clipId, CancellationToken cancellationToken = default);

    // Timeline markers management
    Task<List<TimelineMarker>> GetMarkersAsync(string projectId, string? category = null, double? minTime = null, double? maxTime = null, CancellationToken cancellationToken = default);
    Task<TimelineMarker> GetMarkerAsync(string projectId, string markerId, CancellationToken cancellationToken = default);
    Task<TimelineMarker> CreateMarkerAsync(string projectId, MarkerCreateRequest request, CancellationToken cancellationToken = default);
    Task<TimelineMarker> UpdateMarkerAsync(string projectId, string markerId, MarkerUpdateRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteMarkerAsync(string projectId, string markerId, CancellationToken cancellationToken = default);

    // Macro management
    Task<List<Macro>> GetMacrosAsync(string? projectId = null, CancellationToken cancellationToken = default);
    Task<Macro> GetMacroAsync(string macroId, CancellationToken cancellationToken = default);
    Task<Macro> CreateMacroAsync(Macro macro, CancellationToken cancellationToken = default);
    Task<Macro> UpdateMacroAsync(string macroId, Macro macro, CancellationToken cancellationToken = default);
    Task<bool> DeleteMacroAsync(string macroId, CancellationToken cancellationToken = default);
    Task<bool> ExecuteMacroAsync(string macroId, CancellationToken cancellationToken = default);
    Task<MacroExecutionStatus> GetMacroExecutionStatusAsync(string macroId, CancellationToken cancellationToken = default);

    // Automation curves
    Task<List<AutomationCurve>> GetAutomationCurvesAsync(string trackId, CancellationToken cancellationToken = default);
    Task<AutomationCurve> CreateAutomationCurveAsync(AutomationCurve curve, CancellationToken cancellationToken = default);
    Task<AutomationCurve> UpdateAutomationCurveAsync(string curveId, AutomationCurve curve, CancellationToken cancellationToken = default);
    Task<bool> DeleteAutomationCurveAsync(string curveId, CancellationToken cancellationToken = default);

    // Workflow management (IDEA 33)
    Task<List<Workflow>> GetWorkflowsAsync(int skip = 0, int limit = 100, bool enabledOnly = false, CancellationToken cancellationToken = default);
    Task<Workflow> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<Workflow> CreateWorkflowAsync(WorkflowCreateRequest request, CancellationToken cancellationToken = default);
    Task<Workflow> UpdateWorkflowAsync(string workflowId, WorkflowUpdateRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? inputData = null, CancellationToken cancellationToken = default);

    // Model management
    Task<List<ModelInfo>> GetModelsAsync(string? engine = null, CancellationToken cancellationToken = default);
    Task<ModelInfo> GetModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<ModelInfo> RegisterModelAsync(string engine, string modelName, string modelPath, string? version = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
    Task<ModelVerifyResponse> VerifyModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<ModelInfo> UpdateModelChecksumAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<bool> DeleteModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<StorageStats> GetStorageStatsAsync(CancellationToken cancellationToken = default);

    // Model export/import
    Task<Stream> ExportModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<ModelInfo> ImportModelAsync(Stream modelArchive, string? engine = null, CancellationToken cancellationToken = default);

    // Telemetry and diagnostics (GetTelemetryAsync is defined in Health check section above)

    // Effects chain management
    Task<List<EffectChain>> GetEffectChainsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<EffectChain> GetEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default);
    Task<EffectChain> CreateEffectChainAsync(string projectId, EffectChain chain, CancellationToken cancellationToken = default);
    Task<EffectChain> UpdateEffectChainAsync(string projectId, string chainId, EffectChain chain, CancellationToken cancellationToken = default);
    Task<bool> DeleteEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default);
    Task<EffectProcessResponse> ProcessAudioWithChainAsync(string projectId, string chainId, string audioId, string? outputFilename = null, CancellationToken cancellationToken = default);

    // Effect presets
    Task<List<EffectPreset>> GetEffectPresetsAsync(string? effectType = null, CancellationToken cancellationToken = default);
    Task<EffectPreset> CreateEffectPresetAsync(EffectPreset preset, CancellationToken cancellationToken = default);
    Task<bool> DeleteEffectPresetAsync(string presetId, CancellationToken cancellationToken = default);

    // Batch processing
    Task<BatchJob> CreateBatchJobAsync(BatchJobRequest request, CancellationToken cancellationToken = default);
    Task<List<BatchJob>> GetBatchJobsAsync(string? projectId = null, JobStatus? status = null, CancellationToken cancellationToken = default);

    // Quality-Based Batch Processing endpoints (IDEA 57)
    Task<BatchQualityReport> GetBatchJobQualityAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchQualityReport> GetBatchQualityReportAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchQualityStatistics> GetBatchQualityStatisticsAsync(string? projectId = null, JobStatus? status = null, CancellationToken cancellationToken = default);
    Task<BatchJob> RetryBatchJobWithQualityAsync(string jobId, BatchRetryWithQualityRequest request, CancellationToken cancellationToken = default);

    // Transcription
    Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default);
    Task<TranscriptionResponse> TranscribeAudioAsync(TranscriptionRequest request, string? projectId = null, CancellationToken cancellationToken = default);
    Task<TranscriptionResponse> GetTranscriptionAsync(string transcriptionId, CancellationToken cancellationToken = default);
    Task<List<TranscriptionResponse>> ListTranscriptionsAsync(string? audioId = null, string? projectId = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteTranscriptionAsync(string transcriptionId, CancellationToken cancellationToken = default);

    // Training
    Task<TrainingDataset> CreateDatasetAsync(string name, string? description = null, List<string>? audioFiles = null, CancellationToken cancellationToken = default);
    Task<List<TrainingDataset>> ListDatasetsAsync(CancellationToken cancellationToken = default);
    Task<TrainingDataset> GetDatasetAsync(string datasetId, CancellationToken cancellationToken = default);
    Task<bool> DeleteDatasetAsync(string datasetId, CancellationToken cancellationToken = default);
    Task<TrainingStatus> StartTrainingAsync(TrainingRequest request, CancellationToken cancellationToken = default);
    Task<TrainingStatus> GetTrainingStatusAsync(string trainingId, CancellationToken cancellationToken = default);
    Task<List<TrainingStatus>> ListTrainingJobsAsync(string? profileId = null, string? status = null, CancellationToken cancellationToken = default);
    Task<bool> CancelTrainingAsync(string trainingId, CancellationToken cancellationToken = default);
    Task<List<TrainingLogEntry>> GetTrainingLogsAsync(string trainingId, int? limit = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteTrainingJobAsync(string trainingId, CancellationToken cancellationToken = default);

    // Training quality monitoring (IDEA 54)
    Task<List<TrainingQualityMetrics>> GetTrainingQualityHistoryAsync(string trainingId, int? limit = null, CancellationToken cancellationToken = default);

    // Multi-engine ensemble synthesis (IDEA 55)
    Task<MultiEngineEnsembleResponse> CreateMultiEngineEnsembleAsync(MultiEngineEnsembleRequest request, CancellationToken cancellationToken = default);
    Task<MultiEngineEnsembleStatus> GetMultiEngineEnsembleStatusAsync(string jobId, CancellationToken cancellationToken = default);

    // Mixer management
    Task<MixerState> GetMixerStateAsync(string projectId, CancellationToken cancellationToken = default);
    Task<MixerState> UpdateMixerStateAsync(string projectId, MixerState state, CancellationToken cancellationToken = default);
    Task<MixerState> ResetMixerStateAsync(string projectId, CancellationToken cancellationToken = default);

    // Mixer sends/returns
    Task<MixerSend> CreateMixerSendAsync(string projectId, MixerSend send, CancellationToken cancellationToken = default);
    Task<MixerSend> UpdateMixerSendAsync(string projectId, string sendId, MixerSend send, CancellationToken cancellationToken = default);
    Task<bool> DeleteMixerSendAsync(string projectId, string sendId, CancellationToken cancellationToken = default);
    Task<MixerReturn> CreateMixerReturnAsync(string projectId, MixerReturn returnBus, CancellationToken cancellationToken = default);
    Task<MixerReturn> UpdateMixerReturnAsync(string projectId, string returnId, MixerReturn returnBus, CancellationToken cancellationToken = default);
    Task<bool> DeleteMixerReturnAsync(string projectId, string returnId, CancellationToken cancellationToken = default);

    // Mixer sub-groups
    Task<MixerSubGroup> CreateMixerSubGroupAsync(string projectId, MixerSubGroup subgroup, CancellationToken cancellationToken = default);
    Task<MixerSubGroup> UpdateMixerSubGroupAsync(string projectId, string subgroupId, MixerSubGroup subgroup, CancellationToken cancellationToken = default);
    Task<bool> DeleteMixerSubGroupAsync(string projectId, string subgroupId, CancellationToken cancellationToken = default);

    // Mixer master
    Task<MixerMaster> UpdateMixerMasterAsync(string projectId, MixerMaster master, CancellationToken cancellationToken = default);

    // Channel routing
    Task<ChannelRouting> UpdateChannelRoutingAsync(string projectId, string channelId, ChannelRouting routing, CancellationToken cancellationToken = default);

    // Mixer presets
    Task<List<MixerPreset>> GetMixerPresetsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<MixerPreset> GetMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default);
    Task<MixerPreset> CreateMixerPresetAsync(string projectId, MixerPreset preset, CancellationToken cancellationToken = default);
    Task<MixerPreset> UpdateMixerPresetAsync(string projectId, string presetId, MixerPreset preset, CancellationToken cancellationToken = default);
    Task<bool> DeleteMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default);
    Task<MixerState> ApplyMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default);

    // Backup and restore
    Task<List<BackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default);
    Task<BackupInfo> GetBackupAsync(string backupId, CancellationToken cancellationToken = default);
    Task<BackupInfo> CreateBackupAsync(BackupCreateRequest request, CancellationToken cancellationToken = default);
    Task<Stream> DownloadBackupAsync(string backupId, CancellationToken cancellationToken = default);
    Task<RestoreResponse> RestoreBackupAsync(string backupId, RestoreRequest request, CancellationToken cancellationToken = default);
    Task<BackupInfo> UploadBackupAsync(Stream backupFile, string? name = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default);

    // Settings management
    Task<SettingsData> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<T?> GetSettingsCategoryAsync<T>(string category, CancellationToken cancellationToken = default) where T : class;
    Task<SettingsData> SaveSettingsAsync(SettingsData settings, CancellationToken cancellationToken = default);
    Task<T> UpdateSettingsCategoryAsync<T>(string category, T categorySettings, CancellationToken cancellationToken = default) where T : class;
    Task<SettingsData> ResetSettingsAsync(CancellationToken cancellationToken = default);

    // Helper methods for SettingsService compatibility
    Task<T?> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default) where T : class;
    Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default);
    Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default);

    // Script editor endpoints
    Task<List<Script>> GetScriptsAsync(string? projectId = null, string? search = null, CancellationToken cancellationToken = default);
    Task<Script> GetScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    Task<Script> CreateScriptAsync(ScriptCreateRequest request, CancellationToken cancellationToken = default);
    Task<Script> UpdateScriptAsync(string scriptId, ScriptUpdateRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    Task<Script> AddSegmentToScriptAsync(string scriptId, ScriptSegment segment, CancellationToken cancellationToken = default);
    Task<bool> RemoveSegmentFromScriptAsync(string scriptId, string segmentId, CancellationToken cancellationToken = default);
    Task<ScriptSynthesisResponse> SynthesizeScriptAsync(string scriptId, CancellationToken cancellationToken = default);

    // Quality management endpoints
    Task<Dictionary<string, QualityPresetInfo>> GetQualityPresetsAsync(CancellationToken cancellationToken = default);
    Task<QualityPresetInfo> GetQualityPresetAsync(string presetName, CancellationToken cancellationToken = default);
    Task<QualityAnalysisResponse> AnalyzeQualityAsync(QualityAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<QualityOptimizationResponse> OptimizeQualityAsync(QualityOptimizationRequest request, CancellationToken cancellationToken = default);
    Task<QualityComparisonResponse> CompareQualityAsync(QualityComparisonRequest request, CancellationToken cancellationToken = default);
    Task<EngineRecommendationResponse> GetEngineRecommendationAsync(EngineRecommendationRequest request, CancellationToken cancellationToken = default);

    // A/B Testing endpoints (IDEA 46)
    Task<ABTestResponse> RunABTestAsync(ABTestRequest request, CancellationToken cancellationToken = default);

    // Quality Benchmarking endpoints (IDEA 52)
    Task<BenchmarkResponse> RunBenchmarkAsync(BenchmarkRequest request, CancellationToken cancellationToken = default);

    // Quality Dashboard endpoint (IDEA 49)
    Task<QualityDashboard> GetQualityDashboardAsync(string? projectId = null, int days = 30, CancellationToken cancellationToken = default);

    // Quality History endpoints (IDEA 30)
    Task<QualityHistoryEntry> StoreQualityHistoryAsync(QualityHistoryRequest request, CancellationToken cancellationToken = default);
    Task<List<QualityHistoryEntry>> GetQualityHistoryAsync(string profileId, int? limit = null, string? startDate = null, string? endDate = null, CancellationToken cancellationToken = default);
    Task<QualityTrends> GetQualityTrendsAsync(string profileId, string timeRange = "30d", CancellationToken cancellationToken = default);

    // Quality Degradation Detection endpoints (IDEA 56)
    Task<QualityDegradationResponse?> GetQualityDegradationAsync(string profileId, int timeWindowDays = 7, double degradationThresholdPercent = 10.0, double criticalThresholdPercent = 25.0, CancellationToken cancellationToken = default);
    Task<QualityBaseline?> GetQualityBaselineAsync(string profileId, int timePeriodDays = 30, CancellationToken cancellationToken = default);

    // Adaptive Quality Optimization endpoints (IDEA 53)
    Task<TextAnalysisResult> AnalyzeTextAsync(string text, string language = "en", CancellationToken cancellationToken = default);
    Task<QualityRecommendation> GetQualityRecommendationAsync(string text, string language = "en", List<string>? availableEngines = null, double? targetQuality = null, CancellationToken cancellationToken = default);

    // Engine-Specific Quality Pipelines endpoints (IDEA 58)
    Task<List<string>> ListQualityPipelinePresetsAsync(string engineId, CancellationToken cancellationToken = default);

    // Engine discovery
    Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default);
    Task<PipelineConfiguration?> GetQualityPipelineAsync(string engineId, string presetName, CancellationToken cancellationToken = default);
    Task<PreviewPipelineResponse> PreviewQualityPipelineAsync(string audioId, string engineId, string? presetName = null, PipelineConfiguration? pipelineConfig = null, CancellationToken cancellationToken = default);
    Task<PipelineComparisonResponse> CompareQualityPipelineAsync(string audioId, string engineId, string? presetName = null, CancellationToken cancellationToken = default);

    // Quality Consistency Monitoring endpoints (IDEA 59)
    Task<bool> SetQualityStandardAsync(string projectId, string standardName, CancellationToken cancellationToken = default);
    Task<bool> RecordQualityMetricsAsync(string projectId, Dictionary<string, object> metrics, string? profileId = null, string? audioId = null, CancellationToken cancellationToken = default);
    Task<QualityConsistencyReport> CheckProjectConsistencyAsync(string projectId, int timePeriodDays = 30, CancellationToken cancellationToken = default);
    Task<AllProjectsConsistencyResponse> CheckAllProjectsConsistencyAsync(int timePeriodDays = 30, CancellationToken cancellationToken = default);
    Task<QualityTrendsResponse> GetProjectQualityTrendsAsync(string projectId, int timePeriodDays = 30, CancellationToken cancellationToken = default);

    // Advanced Quality Metrics Visualization endpoints (IDEA 60)
    Task<QualityHeatmapResponse> GetQualityHeatmapAsync(QualityHeatmapRequest request, CancellationToken cancellationToken = default);
    Task<QualityCorrelationResponse> GetQualityCorrelationsAsync(List<Dictionary<string, object>> qualityData, CancellationToken cancellationToken = default);
    Task<QualityAnomalyResponse> DetectQualityAnomaliesAsync(List<Dictionary<string, object>> qualityData, string metric = "mos_score", double thresholdStd = 2.0, CancellationToken cancellationToken = default);
    Task<QualityPredictionResponse> PredictQualityAsync(QualityPredictionRequest request, CancellationToken cancellationToken = default);
    Task<QualityInsightsResponse> GetQualityInsightsAsync(List<Dictionary<string, object>> qualityData, int timePeriodDays = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a global search across all panels and content types.
    /// Implements IDEA 5: Global Search with Panel Context.
    /// </summary>
    Task<SearchResponse> SearchAsync(string query, string? types = null, int limit = 50, CancellationToken cancellationToken = default);

    // Emotion preset management endpoints
    Task<List<EmotionPreset>> GetEmotionPresetsAsync(CancellationToken cancellationToken = default);
    Task<EmotionPreset> GetEmotionPresetAsync(string presetId, CancellationToken cancellationToken = default);
    Task<EmotionPreset> CreateEmotionPresetAsync(EmotionPresetCreateRequest request, CancellationToken cancellationToken = default);
    Task<EmotionPreset> UpdateEmotionPresetAsync(string presetId, EmotionPresetUpdateRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteEmotionPresetAsync(string presetId, CancellationToken cancellationToken = default);
    Task<List<string>> GetAvailableEmotionsAsync(CancellationToken cancellationToken = default);

    // Batch processing
    Task<bool> DeleteBatchJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchJob> StartBatchJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchJob> CancelBatchJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchQueueStatus> GetBatchQueueStatusAsync(CancellationToken cancellationToken = default);

    // Training datasets
    Task<List<TrainingDataset>> GetTrainingDatasetsAsync(CancellationToken cancellationToken = default);
    Task<TrainingDataset> GetTrainingDatasetAsync(string datasetId, CancellationToken cancellationToken = default);

    // Video generation
    Task<List<string>> ListVideoEnginesAsync(CancellationToken cancellationToken = default);
    Task<VideoGenerateResponse> GenerateVideoAsync(VideoGenerateRequest request, CancellationToken cancellationToken = default);
    Task<VideoUpscaleResponse> UpscaleVideoAsync(VideoUpscaleRequest request, CancellationToken cancellationToken = default);
    Task<VideoInfo> GetVideoInfoAsync(string videoPath, CancellationToken cancellationToken = default);
    Task<VideoEditResponse> EditVideoAsync(VideoEditRequest request, CancellationToken cancellationToken = default);

    // Voice AI Pipeline
    Task<VoiceStudio.App.Core.Models.PipelineProvidersResponse> GetPipelineProvidersAsync(CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Core.Models.PipelineResponse> ProcessPipelineAsync(VoiceStudio.App.Core.Models.PipelineRequest request, CancellationToken cancellationToken = default);

    // Base address property
    System.Uri? BaseAddress { get; }
  }
}