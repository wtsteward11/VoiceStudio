using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models.Orchestration
{
    public class PipelineStep
    {
        [JsonPropertyName("step_id")]
        public string StepId { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "synthesis";

        [JsonPropertyName("engine")]
        public string? Engine { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, object>? Parameters { get; set; }

        [JsonPropertyName("condition")]
        public Dictionary<string, object>? Condition { get; set; }
    }

    public class QualityThresholdPolicy
    {
        [JsonPropertyName("min_mos")]
        public double MinMos { get; set; } = 3.5;

        [JsonPropertyName("min_similarity")]
        public double MinSimilarity { get; set; } = 0.7;

        [JsonPropertyName("min_naturalness")]
        public double MinNaturalness { get; set; } = 0.8;

        [JsonPropertyName("max_artifact_score")]
        public double MaxArtifactScore { get; set; } = 0.05;

        [JsonPropertyName("min_snr_db")]
        public double MinSnrDb { get; set; } = 30.0;

        [JsonPropertyName("auto_enhance_if_below")]
        public bool AutoEnhanceIfBelow { get; set; } = true;

        [JsonPropertyName("fail_if_unmet")]
        public bool FailIfUnmet { get; set; }
    }

    public class RetryPolicy
    {
        [JsonPropertyName("max_attempts")]
        public int MaxAttempts { get; set; } = 3;

        [JsonPropertyName("adaptive_adjustment")]
        public bool AdaptiveAdjustment { get; set; } = true;

        [JsonPropertyName("fallback_engines_enabled")]
        public bool FallbackEnginesEnabled { get; set; } = true;

        [JsonPropertyName("parameter_mutation_strategy")]
        public string ParameterMutationStrategy { get; set; } = "quality_gradient";
    }

    public class ProductionChain
    {
        [JsonPropertyName("schema_version")]
        public string SchemaVersion { get; set; } = "1.0";

        [JsonPropertyName("chain_id")]
        public string ChainId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Default";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("steps")]
        public List<PipelineStep> Steps { get; set; } = new();

        [JsonPropertyName("quality_policy")]
        public QualityThresholdPolicy QualityPolicy { get; set; } = new();
    }

    public class OrchestrationRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("voice_profile_id")]
        public string VoiceProfileId { get; set; } = string.Empty;

        [JsonPropertyName("reference_audio_path")]
        public string? ReferenceAudioPath { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; } = "en";

        [JsonPropertyName("production_chain")]
        public ProductionChain? ProductionChain { get; set; }

        [JsonPropertyName("strategy")]
        public string Strategy { get; set; } = "auto";

        [JsonPropertyName("target_quality")]
        public QualityThresholdPolicy TargetQuality { get; set; } = new();

        [JsonPropertyName("retry_policy")]
        public RetryPolicy RetryPolicy { get; set; } = new();

        [JsonPropertyName("async_mode")]
        public bool AsyncMode { get; set; } = true;

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = "interactive";
    }

    public class ParameterAdjustment
    {
        [JsonPropertyName("attempt")]
        public int Attempt { get; set; }

        [JsonPropertyName("changes")]
        public Dictionary<string, object>? Changes { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    public class EngineExecutionPlan
    {
        [JsonPropertyName("selected_engine")]
        public string SelectedEngine { get; set; } = string.Empty;

        [JsonPropertyName("fallback_engines")]
        public List<string> FallbackEngines { get; set; } = new();

        [JsonPropertyName("parameter_adjustments")]
        public List<ParameterAdjustment> ParameterAdjustments { get; set; } = new();
    }

    public class OrchestrationQualityMetrics
    {
        [JsonPropertyName("mos_score")]
        public double? MosScore { get; set; }

        [JsonPropertyName("similarity")]
        public double? Similarity { get; set; }

        [JsonPropertyName("naturalness")]
        public double? Naturalness { get; set; }

        [JsonPropertyName("snr_db")]
        public double? SnrDb { get; set; }

        [JsonPropertyName("artifact_score")]
        public double? ArtifactScore { get; set; }

        [JsonPropertyName("has_clicks")]
        public bool? HasClicks { get; set; }

        [JsonPropertyName("has_distortion")]
        public bool? HasDistortion { get; set; }
    }

    public class OrchestrationResponse
    {
        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "queued";

        [JsonPropertyName("engine_used")]
        public string? EngineUsed { get; set; }

        [JsonPropertyName("execution_plan")]
        public EngineExecutionPlan? ExecutionPlan { get; set; }

        [JsonPropertyName("quality_metrics")]
        public OrchestrationQualityMetrics? QualityMetrics { get; set; }

        [JsonPropertyName("enhancements_applied")]
        public List<string> EnhancementsApplied { get; set; } = new();

        [JsonPropertyName("audio_output_url")]
        public string? AudioOutputUrl { get; set; }

        [JsonPropertyName("total_execution_time_ms")]
        public double? TotalExecutionTimeMs { get; set; }

        [JsonPropertyName("retry_count")]
        public int RetryCount { get; set; }
    }

    public class OrchestrationStatusResponse
    {
        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "queued";

        [JsonPropertyName("progress_percent")]
        public double ProgressPercent { get; set; }

        [JsonPropertyName("current_step")]
        public string? CurrentStep { get; set; }

        [JsonPropertyName("engine_active")]
        public string? EngineActive { get; set; }

        [JsonPropertyName("estimated_remaining_ms")]
        public double? EstimatedRemainingMs { get; set; }

        [JsonPropertyName("started_at")]
        public string? StartedAt { get; set; }

        [JsonPropertyName("retry_count")]
        public int RetryCount { get; set; }
    }

    public class StrategyPreset
    {
        [JsonPropertyName("preset_id")]
        public string PresetId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("default_chain")]
        public ProductionChain DefaultChain { get; set; } = new();

        [JsonPropertyName("default_quality_policy")]
        public QualityThresholdPolicy DefaultQualityPolicy { get; set; } = new();

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("is_builtin")]
        public bool IsBuiltin { get; set; }
    }

    public class OrchestrationEvent
    {
        [JsonPropertyName("event_type")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public Dictionary<string, object>? Data { get; set; }
    }

    public class GpuStatusResponse
    {
        [JsonPropertyName("gpu_available")]
        public bool GpuAvailable { get; set; }

        [JsonPropertyName("total_vram_mb")]
        public double TotalVramMb { get; set; }

        [JsonPropertyName("used_vram_mb")]
        public double UsedVramMb { get; set; }

        [JsonPropertyName("free_vram_mb")]
        public double FreeVramMb { get; set; }

        [JsonPropertyName("utilization_percent")]
        public double UtilizationPercent { get; set; }

        [JsonPropertyName("active_engine_count")]
        public int ActiveEngineCount { get; set; }

        [JsonPropertyName("can_schedule")]
        public bool CanSchedule { get; set; } = true;
    }
}
