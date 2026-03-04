# Backend API Endpoint Test Report
## Comprehensive Testing of All 783 API Endpoints

**Date:** 2026-03-03 12:37:48
**Worker:** Worker 3 (Testing/Quality/Documentation Specialist)
**Test Suite:** Comprehensive API Endpoint Tests

---

## 📊 Executive Summary

**Total Route Files:** 121
**Total Endpoints:** 783
**Files with Code Quality Violations:** 1 (0.8%)
**Endpoints with Implementation Issues:** 711 (90.8%)

---

## 📋 Endpoints by HTTP Method


### DELETE (67 endpoints)

| Path | File | Function | Implementation |
|------|------|----------|----------------|
| /api/advanced-spectrogram/views/{view_id} | advanced_spectrogram.py | delete_spectrogram_view | ❌ |
| /api/ai-production-assistant/session/{session_id} | ai_production_assistant.py | delete_session | ❌ |
| /api/api-key-manager/{key_id} | api_key_manager.py | delete_api_key | ❌ |
| /api/assistant/conversations/{conversation_id} | assistant.py | delete_conversation | ❌ |
| /api/automation/{curve_id} | automation.py | delete_automation_curve | ❌ |
| /api/automation/{curve_id}/points/{point_index} | automation.py | delete_automation_point | ❌ |
| /api/backup/{backup_id} | backup.py | delete_backup | ❌ |
| /api/batch/jobs/{job_id} | batch.py | delete_batch_job | ❌ |
| /api/effects/chains/{chain_id} | effects.py | delete_effect_chain | ✅ |
| /api/effects/presets/{preset_id} | effects.py | delete_effect_preset | ✅ |
| /api/embedding-explorer/embeddings/{embedding_id} | embedding_explorer.py | delete_embedding | ❌ |
| /api/emotion/preset/{preset_id} | emotion.py | delete_preset | ❌ |
| /api/ensemble/{job_id} | ensemble.py | delete_ensemble_job | ❌ |
| /api/errors/resolved | errors.py | clear_resolved_errors | ❌ |
| /api/experiments/{experiment_id} | experiments.py | delete_experiment | ❌ |
| /api/face-swap/jobs/{job_id} | face_swap.py | delete_face_swap_job | ❌ |
| /api/feedback/{feedback_id} | feedback.py | delete_feedback | ❌ |
| /api/help/shortcuts/{key} | help.py | delete_keyboard_shortcut | ❌ |
| /api/help/topics/{topic_id} | help.py | delete_help_topic | ❌ |
| /api/image-search/history | image_search.py | clear_search_history | ❌ |
| /api/instant-cloning/embeddings/{embedding_id} | instant_cloning.py | delete_embedding | ❌ |
| /api/jobs/{job_id} | jobs.py | delete_job | ❌ |
| /api/lexicon/lexicons/{lexicon_id} | lexicon.py | delete_lexicon | ❌ |
| /api/lexicon/lexicons/{lexicon_id}/entries/{word} | lexicon.py | delete_lexicon_entry | ❌ |
| /api/lexicon/remove/{word} | lexicon.py | remove_lexicon_entry | ❌ |
| /api/library/assets/{asset_id} | library.py | delete_asset | ❌ |
| /api/macros/automation/curves/{curve_id} | macros.py | delete_automation_curve | ✅ |
| /api/macros/automation/{curve_id} | macros.py | delete_track_automation | ✅ |
| /api/macros/{macro_id} | macros.py | delete_macro | ✅ |
| /api/macros/{macro_id}/schedule | macros.py | cancel_macro_schedule | ❌ |
| /api/markers/{marker_id} | markers.py | delete_marker | ❌ |
| /api/mix-assistant/suggestions/{suggestion_id} | mix_assistant.py | dismiss_suggestion | ❌ |
| /api/mixer/presets/{project_id}/{preset_id} | mixer.py | delete_preset | ❌ |
| /api/mixer/state/{project_id}/returns/{return_id} | mixer.py | delete_return | ❌ |
| /api/mixer/state/{project_id}/sends/{send_id} | mixer.py | delete_send | ❌ |
| /api/mixer/state/{project_id}/subgroups/{subgroup_id} | mixer.py | delete_subgroup | ❌ |
| /api/models/{engine}/{model_name} | models.py | delete_model | ❌ |
| /api/multi-speaker-dubbing/projects/{project_id} | multi_speaker_dubbing.py | delete_project | ❌ |
| /api/orchestrator/presets/{preset_id} | orchestrator.py | delete_preset | ❌ |
| /api/plugin-gallery/install/{plugin_id} | plugin_gallery.py | uninstall_plugin | ❌ |
| /api/presets/{preset_id} | presets.py | delete_preset | ❌ |
| /api/profiles/{profile_id} | profiles.py | delete_profile | ✅ |
| /api/projects/{project_id} | projects.py | delete_project | ✅ |
| /api/prosody/configs/{config_id} | prosody.py | delete_prosody_config | ❌ |
| /api/realtime-converter/{session_id} | realtime_converter.py | delete_converter_session | ❌ |
| /api/realtime-visualizer/{session_id} | realtime_visualizer.py | delete_visualizer_session | ❌ |
| /api/recording/{recording_id} | recording.py | cancel_recording | ❌ |
| /api/scenes/{scene_id} | scenes.py | delete_scene | ❌ |
| /api/scenes/{scene_id}/tracks/{track_id} | scenes.py | remove_track_from_scene | ❌ |
| /api/shortcuts/{shortcut_id} | shortcuts.py | delete_shortcut | ❌ |
| /api/spatial-audio/configs/{config_id} | spatial_audio.py | delete_spatial_config | ❌ |
| /api/ssml/{document_id} | ssml.py | delete_ssml_document | ❌ |
| /api/style-transfer/jobs/{job_id} | style_transfer.py | delete_style_transfer_job | ❌ |
| /api/tags/{tag_id} | tags.py | delete_tag | ❌ |
| /api/templates/{template_id} | templates.py | delete_template | ❌ |
| /api/text-speech-editor/sessions/{session_id} | text_speech_editor.py | delete_session | ❌ |
| /api/tracks/{track_id} | tracks.py | delete_track | ✅ |
| /api/tracks/{track_id}/clips/{clip_id} | tracks.py | delete_clip | ✅ |
| /api/training/{training_id} | training.py | delete_training_job | ❌ |
| /api/transcribe/{transcription_id} | transcribe.py | delete_transcription | ❌ |
| /api/translation/projects/{project_id} | translation.py | delete_project | ❌ |
| /api/upscaling/jobs/{job_id} | upscaling.py | delete_upscaling_job | ❌ |
| /api/video-enhance/cancel/{job_id} | video_enhance.py | cancel_job | ❌ |
| /api/voice-cloning-wizard/{job_id} | voice_cloning_wizard.py | delete_wizard_job | ❌ |
| /api/voice-effects/hotkeys/{hotkey} | voice_effects.py | remove_hotkey | ❌ |
| /api/voice-morph/configs/{config_id} | voice_morph.py | delete_morph_config | ❌ |
| /api/workflows/{workflow_id} | workflows.py | delete_workflow | ❌ |

### GET (368 endpoints)

| Path | File | Function | Implementation |
|------|------|----------|----------------|
| /api/advanced-settings/category/{category} | advanced_settings.py | get_settings_category | ❌ |
| /api/advanced-spectrogram/export/{view_id} | advanced_spectrogram.py | export_spectrogram | ❌ |
| /api/advanced-spectrogram/view-types | advanced_spectrogram.py | get_view_types | ❌ |
| /api/advanced-spectrogram/views/{view_id} | advanced_spectrogram.py | get_spectrogram_view | ❌ |
| /api/ai-enhancement/modes | ai_enhancement.py | list_modes | ❌ |
| /api/ai-enhancement/presets | ai_enhancement.py | list_presets | ❌ |
| /api/ai-production-assistant/context | ai_production_assistant.py | get_context | ❌ |
| /api/ai-production-assistant/session/{session_id} | ai_production_assistant.py | get_session | ❌ |
| /api/analytics/categories | analytics.py | list_analytics_categories | ❌ |
| /api/analytics/explain-quality | analytics.py | explain_quality_prediction | ❌ |
| /api/analytics/export/metrics/{category} | analytics.py | export_category_metrics | ❌ |
| /api/analytics/export/summary | analytics.py | export_analytics_summary | ❌ |
| /api/analytics/metrics/{category} | analytics.py | get_category_metrics | ❌ |
| /api/analytics/summary | analytics.py | get_analytics_summary | ❌ |
| /api/analytics/visualize-quality | analytics.py | visualize_quality_metrics | ❌ |
| /api/assistant-run/actions | assistant_run.py | list_actions | ❌ |
| /api/assistant-run/actions/{action_id} | assistant_run.py | get_action | ❌ |
| /api/assistant/conversations | assistant.py | list_conversations | ❌ |
| /api/assistant/conversations/{conversation_id} | assistant.py | get_conversation | ❌ |
| /api/assistant/providers | assistant.py | list_providers | ❌ |
| /api/audio-analysis/{audio_id} | audio_analysis.py | get_audio_analysis | ❌ |
| /api/audio-analysis/{audio_id}/compare | audio_analysis.py | compare_audio_analysis | ❌ |
| /api/audio-analysis/{audio_id}/metadata | audio_analysis.py | get_audio_metadata | ❌ |
| /api/audio-analysis/{audio_id}/pitch | audio_analysis.py | get_pitch_analysis | ❌ |
| /api/audio-analysis/{audio_id}/wavelet | audio_analysis.py | get_wavelet_analysis | ❌ |
| /api/audio-audit/all | audio_audit.py | audit_all_modules | ✅ |
| /api/audio-audit/needing-attention | audio_audit.py | get_modules_needing_attention | ✅ |
| /api/audio-audit/report | audio_audit.py | generate_enhancement_report | ✅ |
| /api/audio-audit/summary | audio_audit.py | get_audit_summary | ✅ |
| /api/audio/formats | audio.py | get_supported_formats | ❌ |
| /api/audio/loudness | audio.py | get_loudness_data | ✅ |
| /api/audio/meters | audio.py | get_audio_meters | ✅ |
| /api/audio/phase | audio.py | get_phase_data | ✅ |
| /api/audio/radar | audio.py | get_radar_data | ✅ |
| /api/audio/spectrogram | audio.py | get_spectrogram_data | ✅ |
| /api/audio/waveform | audio.py | get_waveform_data | ✅ |
| /api/auth/me | auth.py | get_current_user_info | ❌ |
| /api/automation/tracks | automation.py | list_automation_tracks | ❌ |
| /api/automation/tracks/{track_id}/parameters | automation.py | get_track_parameters | ❌ |
| /api/automation/{curve_id} | automation.py | get_automation_curve | ❌ |
| /api/backup/{backup_id} | backup.py | get_backup_info | ❌ |
| /api/backup/{backup_id}/download | backup.py | download_backup | ❌ |
| /api/batch/jobs | batch.py | list_batch_jobs | ❌ |
| /api/batch/jobs/{job_id} | batch.py | get_batch_job | ❌ |
| /api/batch/jobs/{job_id}/quality | batch.py | get_batch_job_quality | ❌ |
| /api/batch/jobs/{job_id}/quality-report | batch.py | get_batch_quality_report | ❌ |
| /api/batch/quality/statistics | batch.py | get_batch_quality_statistics | ❌ |
| /api/batch/queue/status | batch.py | get_queue_status | ❌ |
| /api/consent/voice/{voice_id} | consent.py | list_consents_for_voice | ❌ |
| /api/consent/{consent_id} | consent.py | get_consent | ❌ |
| /api/dataset-editor/{dataset_id} | dataset_editor.py | get_dataset_detail | ❌ |
| /api/diagnostics/categories | diagnostics.py | get_categories | ❌ |
| /api/diagnostics/checks | diagnostics.py | get_checks | ❌ |
| /api/diagnostics/environment | diagnostics.py | get_environment_info | ❌ |
| /api/diagnostics/recommendations | diagnostics.py | get_recommendations | ❌ |
| /api/diagnostics/run | diagnostics.py | run_diagnostics | ❌ |
| /api/diagnostics/status | diagnostics.py | get_quick_status | ❌ |
| /api/drift/history | drift.py | get_drift_history | ❌ |
| /api/drift/status | drift.py | get_drift_status | ❌ |
| /api/effects/chains | effects.py | list_effect_chains | ✅ |
| /api/effects/chains/{chain_id} | effects.py | get_effect_chain | ✅ |
| /api/effects/presets | effects.py | list_effect_presets | ✅ |
| /api/embedding-explorer/embeddings | embedding_explorer.py | list_embeddings | ❌ |
| /api/embedding-explorer/embeddings/{embedding_id} | embedding_explorer.py | get_embedding | ❌ |
| /api/emotion-style/emotions | emotion_style.py | get_emotion_presets | ❌ |
| /api/emotion-style/styles | emotion_style.py | get_style_presets | ❌ |
| /api/emotion/list | emotion.py | list_emotions | ❌ |
| /api/emotion/preset/list | emotion.py | list_presets | ❌ |
| /api/emotion/preset/{preset_id} | emotion.py | get_preset | ❌ |
| /api/engine-audit/all | engine_audit.py | audit_all_engines | ✅ |
| /api/engine-audit/needing-attention | engine_audit.py | get_engines_needing_attention | ✅ |
| /api/engine-audit/report | engine_audit.py | generate_enhancement_report | ✅ |
| /api/engine-audit/summary | engine_audit.py | get_audit_summary | ✅ |
| /api/engine/telemetry | engine.py | telemetry | ❌ |
| /api/engine/telemetry/history | engine.py | get_telemetry_history | ❌ |
| /api/engines/compare | engines.py | compare_engines | ❌ |
| /api/engines/config | engines.py | get_engine_configuration | ❌ |
| /api/engines/config/defaults | engines.py | get_default_engines | ❌ |
| /api/engines/config/gpu/settings | engines.py | get_gpu_settings | ❌ |
| /api/engines/config/{engine_id} | engines.py | get_engine_config | ❌ |
| /api/engines/list | engines.py | list_engines | ❌ |
| /api/engines/preflight | engines.py | preflight | ❌ |
| /api/engines/{engine_id}/schema | engines.py | get_engine_schema | ❌ |
| /api/engines/{engine_id}/status | engines.py | get_engine_status | ❌ |
| /api/engines/{engine_id}/voices | engines.py | get_engine_voices | ❌ |
| /api/ensemble/multi-engine/{job_id} | ensemble.py | get_multi_engine_ensemble_status | ❌ |
| /api/ensemble/{job_id} | ensemble.py | get_ensemble_status | ❌ |
| /api/errors/aggregates | errors.py | get_error_aggregates | ❌ |
| /api/errors/categories | errors.py | get_error_categories | ❌ |
| /api/errors/rate | errors.py | get_error_rate | ❌ |
| /api/errors/recent | errors.py | get_recent_errors | ❌ |
| /api/errors/summary | errors.py | get_error_summary | ❌ |
| /api/eval-abx/results | eval_abx.py | results | ❌ |
| /api/eval-abx/sessions/{session_id} | eval_abx.py | get_session | ❌ |
| /api/experiments/{experiment_id} | experiments.py | get_experiment | ❌ |
| /api/experiments/{experiment_id}/events | experiments.py | get_experiment_events | ❌ |
| /api/experiments/{experiment_id}/stats | experiments.py | get_experiment_stats | ❌ |
| /api/face-swap/engines | face_swap.py | list_face_swap_engines | ❌ |
| /api/face-swap/jobs | face_swap.py | list_face_swap_jobs | ❌ |
| /api/face-swap/jobs/{job_id} | face_swap.py | get_face_swap_job | ❌ |
| /api/face-swap/queue/status | face_swap.py | get_queue_status | ❌ |
| /api/feedback/ | feedback.py | list_feedback | ❌ |
| /api/feedback/stats/summary | feedback.py | get_feedback_stats | ❌ |
| /api/feedback/{feedback_id} | feedback.py | get_feedback | ❌ |
| /api/gpu-status/devices | gpu_status.py | list_gpu_devices | ❌ |
| /api/gpu-status/devices/{device_id} | gpu_status.py | get_gpu_device | ❌ |
| /api/health/ | health.py | health_check | ❌ |
| /api/health/circuit-breakers | health.py | circuit_breaker_health | ✅ |
| /api/health/dashboard | health.py | health_dashboard | ❌ |
| /api/health/dependencies | health.py | dependency_check | ✅ |
| /api/health/detailed | health.py | detailed_health_check | ❌ |
| /api/health/engines | health.py | engine_health | ✅ |
| /api/health/features | health.py | get_feature_status | ❌ |
| /api/health/live | health.py | live_check | ✅ |
| /api/health/liveness | health.py | liveness_check | ✅ |
| /api/health/performance | health.py | performance_metrics | ✅ |
| /api/health/performance/{endpoint:path} | health.py | endpoint_performance_metrics | ✅ |
| /api/health/preflight | health.py | preflight_check | ✅ |
| /api/health/readiness | health.py | readiness_check | ❌ |
| /api/health/ready | health.py | ready_check | ❌ |
| /api/health/resources | health.py | resource_usage | ✅ |
| /api/health/simple | health.py | simple_health_check | ✅ |
| /api/health/summary | health.py | health_summary | ❌ |
| /api/help/categories | help.py | get_help_categories | ❌ |
| /api/help/panel/{panel_id} | help.py | get_panel_help | ❌ |
| /api/help/search | help.py | search_help | ❌ |
| /api/help/shortcuts | help.py | get_keyboard_shortcuts | ❌ |
| /api/help/topics | help.py | get_help_topics | ❌ |
| /api/help/topics/{topic_id} | help.py | get_help_topic | ❌ |
| /api/huggingface-fix/status | huggingface_fix.py | status | ✅ |
| /api/image-gen/engines/list | image_gen.py | list_engines | ❌ |
| /api/image-gen/{image_id} | image_gen.py | get_image | ❌ |
| /api/image-search/categories | image_search.py | list_categories | ❌ |
| /api/image-search/colors | image_search.py | list_colors | ❌ |
| /api/image-search/history | image_search.py | get_search_history | ❌ |
| /api/image-search/sources | image_search.py | list_image_sources | ❌ |
| /api/img-sampler/samplers | img_sampler.py | list_samplers | ❌ |
| /api/img-sampler/samplers/{sampler_name} | img_sampler.py | get_sampler_info | ❌ |
| /api/instant-cloning/embeddings | instant_cloning.py | list_embeddings | ❌ |
| /api/integrations/batch/{job_id} | integrations.py | get_batch_status | ❌ |
| /api/integrations/daw/available | integrations.py | get_available_daws | ❌ |
| /api/integrations/daw/presets | integrations.py | get_daw_export_presets | ❌ |
| /api/integrations/sync/status | integrations.py | get_sync_status | ❌ |
| /api/integrations/video/available | integrations.py | get_available_video_editors | ❌ |
| /api/integrations/workflows | integrations.py | list_workflows | ❌ |
| /api/integrations/workflows/{execution_id} | integrations.py | get_workflow_status | ❌ |
| /api/jobs/queue/status | jobs.py | get_job_queue_status_alias | ❌ |
| /api/jobs/status | jobs.py | get_job_queue_status | ❌ |
| /api/jobs/summary | jobs.py | get_job_summary | ❌ |
| /api/jobs/{job_id} | jobs.py | get_job | ❌ |
| /api/lexicon/lexicons | lexicon.py | list_lexicons | ❌ |
| /api/lexicon/lexicons/{lexicon_id} | lexicon.py | get_lexicon | ❌ |
| /api/lexicon/lexicons/{lexicon_id}/entries | lexicon.py | list_lexicon_entries | ❌ |
| /api/lexicon/list | lexicon.py | list_lexicon_entries_simple | ❌ |
| /api/library/assets | library.py | search_assets | ❌ |
| /api/library/assets/{asset_id} | library.py | get_asset | ❌ |
| /api/library/folders | library.py | get_folders | ❌ |
| /api/library/summary | library.py | get_library_summary | ❌ |
| /api/library/types | library.py | get_asset_types | ❌ |
| /api/lip-sync/engines | lip_sync.py | list_engines | ❌ |
| /api/lip-sync/engines/{engine_id}/status | lip_sync.py | get_engine_status | ❌ |
| /api/lip-sync/quality-settings | lip_sync.py | list_quality_settings | ❌ |
| /api/macros/automation/curves | macros.py | list_automation_curves | ✅ |
| /api/macros/automation/{track_id} | macros.py | list_track_automation | ✅ |
| /api/macros/{macro_id} | macros.py | get_macro | ✅ |
| /api/macros/{macro_id}/execution-status | macros.py | get_macro_execution_status_alias | ✅ |
| /api/macros/{macro_id}/schedule | macros.py | get_macro_schedule | ❌ |
| /api/macros/{macro_id}/status | macros.py | get_macro_execution_status | ✅ |
| /api/markers/categories/list | markers.py | get_categories | ❌ |
| /api/markers/{marker_id} | markers.py | get_marker | ❌ |
| /api/marketplace/downloads/{plugin_id} | marketplace.py | get_download_count | ❌ |
| /api/marketplace/publishers/{publisher_id} | marketplace.py | get_publisher | ❌ |
| /api/marketplace/review-queue | marketplace.py | get_review_queue | ❌ |
| /api/marketplace/reviews/{plugin_id} | marketplace.py | get_plugin_reviews | ❌ |
| /api/marketplace/reviews/{plugin_id}/mine | marketplace.py | get_my_review | ❌ |
| /api/mix-assistant/suggestions | mix_assistant.py | list_suggestions | ❌ |
| /api/mix-assistant/suggestions/{suggestion_id} | mix_assistant.py | get_suggestion | ❌ |
| /api/mixer/meters/{project_id} | mixer.py | get_mixer_meters | ❌ |
| /api/mixer/presets/{project_id} | mixer.py | list_presets | ❌ |
| /api/mixer/presets/{project_id}/{preset_id} | mixer.py | get_preset | ❌ |
| /api/mixer/state/{project_id} | mixer.py | get_mixer_state | ❌ |
| /api/ml-optimization/methods | ml_optimization.py | get_available_methods | ❌ |
| /api/model-inspect/inspect/layers | model_inspect.py | list_layers | ❌ |
| /api/models/registry | models.py | list_registry_models | ❌ |
| /api/models/registry/{engine_id} | models.py | get_registry_engine_models | ❌ |
| /api/models/stats/cache | models.py | get_cache_stats | ❌ |
| /api/models/stats/storage | models.py | get_storage_stats | ❌ |
| /api/models/{engine}/{model_name} | models.py | get_model | ❌ |
| /api/models/{engine}/{model_name}/export | models.py | export_model | ❌ |
| /api/monitoring/alerts | monitoring.py | get_active_alerts | ❌ |
| /api/monitoring/diagnostics | monitoring.py | run_diagnostics | ❌ |
| /api/monitoring/errors | monitoring.py | get_recent_errors | ❌ |
| /api/monitoring/health | monitoring.py | health_check | ❌ |
| /api/monitoring/metrics | monitoring.py | get_metrics | ❌ |
| /api/monitoring/metrics/prometheus | monitoring.py | get_prometheus_metrics | ❌ |
| /api/monitoring/performance | monitoring.py | get_performance_stats | ❌ |
| /api/multi-speaker-dubbing/projects | multi_speaker_dubbing.py | list_projects | ❌ |
| /api/multi-speaker-dubbing/projects/{project_id} | multi_speaker_dubbing.py | get_project | ❌ |
| /api/multi-speaker-dubbing/projects/{project_id}/speakers | multi_speaker_dubbing.py | get_project_speakers | ❌ |
| /api/multi-voice-generator/{job_id}/results | multi_voice_generator.py | get_multi_voice_results | ❌ |
| /api/multi-voice-generator/{job_id}/status | multi_voice_generator.py | get_multi_voice_status | ❌ |
| /api/multilingual/languages | multilingual.py | get_language_configs | ❌ |
| /api/multilingual/languages/{config_id} | multilingual.py | get_language_config | ❌ |
| /api/multilingual/supported | multilingual.py | get_supported_languages | ❌ |
| /api/nr/noise-prints | nr.py | list_noise_prints | ❌ |
| /api/orchestrator/debug/{job_id} | orchestrator.py | get_debug_info | ❌ |
| /api/orchestrator/gpu-status | orchestrator.py | get_gpu_status | ❌ |
| /api/orchestrator/presets | orchestrator.py | list_presets | ❌ |
| /api/orchestrator/scheduler-status | orchestrator.py | get_scheduler_status | ❌ |
| /api/orchestrator/status/{job_id} | orchestrator.py | get_status | ❌ |
| /api/orchestrator/strategies | orchestrator.py | list_strategies | ❌ |
| /api/pdf/health | pdf.py | pdf_health_check | ❌ |
| /api/pdf/page-count | pdf.py | get_page_count | ❌ |
| /api/pipeline/metrics | pipeline.py | get_pipeline_metrics | ❌ |
| /api/pipeline/providers | pipeline.py | list_pipeline_providers | ❌ |
| /api/plugin-gallery/catalog | plugin_gallery.py | get_catalog | ❌ |
| /api/plugin-gallery/catalog/featured | plugin_gallery.py | get_featured_plugins | ❌ |
| /api/plugin-gallery/catalog/search | plugin_gallery.py | search_plugins | ❌ |
| /api/plugin-gallery/catalog/{plugin_id} | plugin_gallery.py | get_plugin_details | ❌ |
| /api/plugin-gallery/installed | plugin_gallery.py | list_installed_plugins | ❌ |
| /api/plugin-gallery/updates | plugin_gallery.py | check_for_updates | ❌ |
| /api/plugin-health/audit | plugin_health.py | query_audit_events | ❌ |
| /api/plugin-health/audit/plugins/{plugin_id} | plugin_health.py | get_plugin_audit_trail | ❌ |
| /api/plugin-health/audit/recent | plugin_health.py | get_recent_audit_events | ❌ |
| /api/plugin-health/audit/summary | plugin_health.py | get_audit_summary | ❌ |
| /api/plugin-health/metrics | plugin_health.py | query_metrics | ❌ |
| /api/plugin-health/metrics/aggregated | plugin_health.py | get_aggregated_metrics | ❌ |
| /api/plugin-health/metrics/export | plugin_health.py | export_metrics | ❌ |
| /api/plugin-health/metrics/storage | plugin_health.py | get_storage_stats | ❌ |
| /api/plugin-health/plugins | plugin_health.py | list_plugin_health | ❌ |
| /api/plugin-health/plugins/{plugin_id} | plugin_health.py | get_plugin_health | ❌ |
| /api/plugin-health/system | plugin_health.py | get_system_health | ❌ |
| /api/plugin-health/unhealthy | plugin_health.py | list_unhealthy_plugins | ❌ |
| /api/plugins/wasm | plugins.py | list_wasm_plugins | ❌ |
| /api/plugins/{plugin_id} | plugins.py | get_plugin | ❌ |
| /api/plugins/{plugin_id}/config | plugins.py | get_plugin_config | ❌ |
| /api/plugins/{plugin_id}/manifest | plugins.py | get_plugin_manifest | ❌ |
| /api/plugins/{plugin_id}/signature | plugins.py | verify_plugin_signature | ❌ |
| /api/plugins/{plugin_id}/wasm/capabilities | plugins.py | get_wasm_plugin_capabilities | ❌ |
| /api/plugins/{plugin_id}/wasm/status | plugins.py | get_wasm_plugin_status | ❌ |
| /api/presets/categories/{preset_type} | presets.py | get_categories | ❌ |
| /api/presets/types | presets.py | get_preset_types | ❌ |
| /api/presets/{preset_id} | presets.py | get_preset | ❌ |
| /api/profiles/{profile_id} | profiles.py | get_profile | ✅ |
| /api/projects/{project_id} | projects.py | get_project | ✅ |
| /api/projects/{project_id}/audio | projects.py | list_project_audio | ✅ |
| /api/projects/{project_id}/audio/{filename} | projects.py | get_project_audio | ✅ |
| /api/prosody/configs | prosody.py | list_prosody_configs | ❌ |
| /api/prosody/configs/{config_id} | prosody.py | get_prosody_config | ❌ |
| /api/quality-pipelines/engines/{engine_id}/presets | quality_pipelines.py | list_presets | ❌ |
| /api/quality-pipelines/engines/{engine_id}/presets/{preset_name} | quality_pipelines.py | get_preset | ❌ |
| /api/quality/baseline/{profile_id} | quality.py | get_quality_baseline | ❌ |
| /api/quality/consistency/all | quality.py | check_all_projects_consistency | ❌ |
| /api/quality/consistency/{project_id} | quality.py | check_project_consistency | ❌ |
| /api/quality/consistency/{project_id}/trends | quality.py | get_project_quality_trends | ❌ |
| /api/quality/dashboard | quality.py | get_quality_dashboard | ❌ |
| /api/quality/degradation/{profile_id} | quality.py | check_quality_degradation | ❌ |
| /api/quality/engine-recommendation | quality.py | get_engine_recommendation | ❌ |
| /api/quality/history/{profile_id} | quality.py | get_quality_history | ❌ |
| /api/quality/history/{profile_id}/trends | quality.py | get_quality_trends | ❌ |
| /api/quality/presets | quality.py | list_presets | ❌ |
| /api/quality/presets/{preset_name} | quality.py | get_preset | ❌ |
| /api/realtime-converter/{session_id} | realtime_converter.py | get_converter_session | ❌ |
| /api/realtime-converter/{session_id}/latency | realtime_converter.py | get_session_latency | ❌ |
| /api/realtime-converter/{session_id}/quality | realtime_converter.py | get_session_quality | ❌ |
| /api/realtime-visualizer/{session_id} | realtime_visualizer.py | get_visualizer_session | ❌ |
| /api/recording/devices | recording.py | get_recording_devices | ❌ |
| /api/recording/{recording_id}/status | recording.py | get_recording_status | ❌ |
| /api/rvc/audio/{audio_id} | rvc.py | get_audio | ❌ |
| /api/rvc/models | rvc.py | get_models | ❌ |
| /api/safety/categories | safety.py | get_safety_categories | ❌ |
| /api/scenes/{scene_id} | scenes.py | get_scene | ❌ |
| /api/settings/check/dependencies | settings.py | get_system_dependencies | ❌ |
| /api/settings/{category} | settings.py | get_settings_category | ❌ |
| /api/shortcuts/categories | shortcuts.py | get_shortcut_categories | ❌ |
| /api/shortcuts/check-conflict | shortcuts.py | check_conflict | ❌ |
| /api/shortcuts/{shortcut_id} | shortcuts.py | get_shortcut | ❌ |
| /api/slo/alerts/active | slo.py | get_active_alerts | ❌ |
| /api/slo/alerts/history | slo.py | get_alert_history | ❌ |
| /api/slo/health | slo.py | get_slo_health | ❌ |
| /api/slo/{slo_id} | slo.py | get_slo_status | ❌ |
| /api/sonography/color-schemes | sonography.py | get_available_color_schemes | ❌ |
| /api/sonography/perspectives | sonography.py | get_available_perspectives | ❌ |
| /api/sonography/{audio_id} | sonography.py | get_sonography_data | ❌ |
| /api/spatial-audio/configs | spatial_audio.py | list_spatial_configs | ❌ |
| /api/spatial-audio/configs/{config_id} | spatial_audio.py | get_spatial_config | ❌ |
| /api/spectrogram/color-schemes | spectrogram.py | get_color_schemes | ❌ |
| /api/spectrogram/compare | spectrogram.py | compare_spectrograms | ❌ |
| /api/spectrogram/config/{audio_id} | spectrogram.py | get_spectrogram_config | ❌ |
| /api/spectrogram/data/{audio_id} | spectrogram.py | get_spectrogram_data | ❌ |
| /api/spectrogram/export/{audio_id} | spectrogram.py | export_spectrogram | ❌ |
| /api/ssml/{document_id} | ssml.py | get_ssml_document | ❌ |
| /api/style-transfer/jobs | style_transfer.py | list_style_transfer_jobs | ❌ |
| /api/style-transfer/jobs/{job_id} | style_transfer.py | get_style_transfer_job | ❌ |
| /api/style-transfer/presets | style_transfer.py | list_style_presets | ❌ |
| /api/tags/categories/list | tags.py | get_tag_categories | ❌ |
| /api/tags/{tag_id} | tags.py | get_tag | ❌ |
| /api/tags/{tag_id}/usage | tags.py | get_tag_usage | ❌ |
| /api/telemetry/metrics | telemetry.py | get_metrics | ❌ |
| /api/telemetry/slos | telemetry.py | get_slos | ❌ |
| /api/telemetry/spans | telemetry.py | get_recent_spans | ❌ |
| /api/templates/categories/list | templates.py | get_template_categories | ❌ |
| /api/templates/{template_id} | templates.py | get_template | ❌ |
| /api/text-speech-editor/session/{session_id} | text_speech_editor.py | get_edit_session | ❌ |
| /api/text-speech-editor/sessions | text_speech_editor.py | list_sessions | ❌ |
| /api/timeline/state | timeline.py | get_timeline_state | ❌ |
| /api/tracing/errors | tracing.py | get_error_spans | ❌ |
| /api/tracing/operations | tracing.py | get_operation_statistics | ❌ |
| /api/tracing/recent | tracing.py | get_recent_spans | ❌ |
| /api/tracing/slow-spans | tracing.py | get_slow_spans | ❌ |
| /api/tracing/summary | tracing.py | get_trace_summary | ❌ |
| /api/tracing/trace/{trace_id}/tree | tracing.py | get_trace_tree | ❌ |
| /api/tracks/{track_id} | tracks.py | get_track | ✅ |
| /api/training-audit/all | training_audit.py | audit_all_modules | ✅ |
| /api/training-audit/needing-attention | training_audit.py | get_modules_needing_attention | ✅ |
| /api/training-audit/report | training_audit.py | generate_enhancement_report | ✅ |
| /api/training-audit/summary | training_audit.py | get_audit_summary | ✅ |
| /api/training/datasets | training.py | list_datasets | ❌ |
| /api/training/datasets/{dataset_id} | training.py | get_dataset | ❌ |
| /api/training/exports/{export_id}/download | training.py | download_export | ❌ |
| /api/training/logs/{training_id} | training.py | get_training_logs | ❌ |
| /api/training/status | training.py | list_training_jobs | ❌ |
| /api/training/status/{training_id} | training.py | get_training_status | ❌ |
| /api/training/{training_id}/quality-history | training.py | get_training_quality_history | ❌ |
| /api/transcribe/ | transcribe.py | list_transcriptions | ❌ |
| /api/transcribe/engines | transcribe.py | list_transcription_engines | ❌ |
| /api/transcribe/languages | transcribe.py | get_supported_languages | ❌ |
| /api/transcribe/{transcription_id} | transcribe.py | get_transcription | ❌ |
| /api/translation/languages | translation.py | list_languages | ❌ |
| /api/translation/models | translation.py | list_transcription_models | ❌ |
| /api/translation/projects | translation.py | list_projects | ❌ |
| /api/translation/projects/{project_id} | translation.py | get_project | ❌ |
| /api/translation/providers | translation.py | list_translation_providers | ❌ |
| /api/upscaling/engines | upscaling.py | list_upscaling_engines | ❌ |
| /api/upscaling/export/{job_id} | upscaling.py | export_upscaled_media | ❌ |
| /api/upscaling/jobs | upscaling.py | list_upscaling_jobs | ❌ |
| /api/upscaling/jobs/{job_id} | upscaling.py | get_upscaling_job | ❌ |
| /api/version/ | version.py | get_versions | ❌ |
| /api/version/compatibility | version.py | check_compatibility | ❌ |
| /api/version/current | version.py | get_current_version | ❌ |
| /api/version/detect | version.py | detect_version | ❌ |
| /api/version/endpoints | version.py | get_versioned_endpoints | ❌ |
| /api/video-edit/info | video_edit.py | get_video_info_endpoint | ❌ |
| /api/video-enhance/capabilities | video_enhance.py | get_capabilities | ❌ |
| /api/video-enhance/jobs | video_enhance.py | list_jobs | ❌ |
| /api/video-enhance/status/{job_id} | video_enhance.py | get_job_status | ❌ |
| /api/video-gen/engines/list | video_gen.py | list_engines | ❌ |
| /api/video-gen/{video_id} | video_gen.py | get_video | ❌ |
| /api/video-gen/{video_id}/quality | video_gen.py | get_video_quality | ❌ |
| /api/voice-browser/languages | voice_browser.py | get_available_languages | ❌ |
| /api/voice-browser/tags | voice_browser.py | get_available_tags | ❌ |
| /api/voice-browser/voices | voice_browser.py | search_voices | ❌ |
| /api/voice-browser/voices/{voice_id} | voice_browser.py | get_voice_summary | ❌ |
| /api/voice-cloning-wizard/{job_id}/status | voice_cloning_wizard.py | get_wizard_status | ❌ |
| /api/voice-effects/audio-devices | voice_effects.py | list_audio_devices | ❌ |
| /api/voice-effects/categories | voice_effects.py | list_effect_categories | ❌ |
| /api/voice-effects/hotkeys | voice_effects.py | list_hotkeys | ❌ |
| /api/voice-effects/presets | voice_effects.py | list_effect_presets | ❌ |
| /api/voice-effects/presets/{preset_id} | voice_effects.py | get_effect_preset | ❌ |
| /api/voice-morph/configs | voice_morph.py | list_morph_configs | ❌ |
| /api/voice-morph/configs/{config_id} | voice_morph.py | get_morph_config | ❌ |
| /api/voice-speech/backends | voice_speech.py | get_available_backends | ❌ |
| /api/voice-speech/{audio_id}/voice-activity | voice_speech.py | detect_voice_activity | ❌ |
| /api/waveform/analysis/{audio_id} | waveform.py | analyze_waveform | ❌ |
| /api/waveform/compare | waveform.py | compare_waveforms | ❌ |
| /api/waveform/config/{audio_id} | waveform.py | get_waveform_config | ❌ |
| /api/waveform/data/{audio_id} | waveform.py | get_waveform_data | ❌ |
| /api/workflows/{workflow_id} | workflows.py | get_workflow | ❌ |

### PATCH (1 endpoints)

| Path | File | Function | Implementation |
|------|------|----------|----------------|
| /api/feedback/{feedback_id}/status | feedback.py | update_feedback_status | ❌ |

### POST (302 endpoints)

| Path | File | Function | Implementation |
|------|------|----------|----------------|
| /api/-state/items | _state.py | get_state_lock | ✅ |
| /api/advanced-settings/reset | advanced_settings.py | reset_advanced_settings | ❌ |
| /api/advanced-spectrogram/compare | advanced_spectrogram.py | compare_spectrograms | ❌ |
| /api/advanced-spectrogram/generate | advanced_spectrogram.py | generate_advanced_spectrogram | ❌ |
| /api/ai-enhancement/de-reverb | ai_enhancement.py | remove_reverb | ❌ |
| /api/ai-enhancement/enhance | ai_enhancement.py | enhance_audio | ❌ |
| /api/ai-enhancement/isolate-voice | ai_enhancement.py | isolate_voice | ❌ |
| /api/ai-enhancement/repair | ai_enhancement.py | repair_audio_endpoint | ❌ |
| /api/ai-production-assistant/execute | ai_production_assistant.py | execute_command | ❌ |
| /api/ai-production-assistant/query | ai_production_assistant.py | process_query | ❌ |
| /api/api-key-manager/{key_id}/validate | api_key_manager.py | validate_api_key | ❌ |
| /api/articulation/analyze | articulation.py | analyze | ❌ |
| /api/assistant-run/run | assistant_run.py | run | ❌ |
| /api/assistant/chat | assistant.py | chat_with_assistant | ❌ |
| /api/assistant/suggest-tasks | assistant.py | suggest_tasks | ❌ |
| /api/audio-analysis/{audio_id}/analyze | audio_analysis.py | analyze_audio | ❌ |
| /api/audio/export | audio.py | export_audio | ❌ |
| /api/audio/upload | audio.py | upload_audio | ❌ |
| /api/auth/api-keys/generate | auth.py | generate_api_key | ❌ |
| /api/auth/api-keys/revoke | auth.py | revoke_api_key | ❌ |
| /api/auth/login | auth.py | login | ❌ |
| /api/auth/refresh | auth.py | refresh_token | ❌ |
| /api/auth/users | auth.py | create_user | ❌ |
| /api/automation/{curve_id}/points | automation.py | add_automation_point | ❌ |
| /api/backup/upload | backup.py | upload_backup | ❌ |
| /api/backup/{backup_id}/restore | backup.py | restore_backup | ❌ |
| /api/batch/jobs | batch.py | create_batch_job | ❌ |
| /api/batch/jobs/{job_id}/cancel | batch.py | cancel_batch_job | ❌ |
| /api/batch/jobs/{job_id}/retry-with-quality | batch.py | retry_batch_job_with_quality | ❌ |
| /api/batch/jobs/{job_id}/start | batch.py | start_batch_job | ❌ |
| /api/consent/grant/{consent_id} | consent.py | grant_consent | ❌ |
| /api/consent/request | consent.py | request_consent | ❌ |
| /api/consent/revoke | consent.py | revoke_consent | ❌ |
| /api/dataset-editor/{dataset_id}/audio | dataset_editor.py | add_audio_to_dataset | ❌ |
| /api/dataset-editor/{dataset_id}/validate | dataset_editor.py | validate_dataset | ❌ |
| /api/dataset/analyze | dataset.py | analyze_dataset | ❌ |
| /api/dataset/cull | dataset.py | cull | ❌ |
| /api/dataset/export | dataset.py | export_dataset | ❌ |
| /api/dataset/score | dataset.py | score | ❌ |
| /api/dataset/validate | dataset.py | validate_dataset | ❌ |
| /api/diagnostics/save | diagnostics.py | save_diagnostic_report | ❌ |
| /api/drift/baseline | drift.py | set_baseline | ❌ |
| /api/dubbing/sync | dubbing.py | sync | ❌ |
| /api/dubbing/translate | dubbing.py | translate | ❌ |
| /api/effects/chains | effects.py | create_effect_chain | ✅ |
| /api/effects/chains/{chain_id}/process | effects.py | process_audio_with_chain | ✅ |
| /api/effects/presets | effects.py | create_effect_preset | ✅ |
| /api/embedding-explorer/cluster | embedding_explorer.py | cluster_embeddings | ❌ |
| /api/embedding-explorer/compare | embedding_explorer.py | compare_embeddings | ❌ |
| /api/embedding-explorer/extract | embedding_explorer.py | extract_embedding | ❌ |
| /api/embedding-explorer/visualize | embedding_explorer.py | visualize_embeddings | ❌ |
| /api/emotion-style/apply | emotion_style.py | apply_emotion_style | ❌ |
| /api/emotion/analyze | emotion.py | analyze | ❌ |
| /api/emotion/apply | emotion.py | apply | ❌ |
| /api/emotion/apply-extended | emotion.py | apply_extended | ❌ |
| /api/emotion/preset/save | emotion.py | save_preset | ❌ |
| /api/emotion/preview | emotion.py | preview_emotion | ❌ |
| /api/engine/telemetry/record | engine.py | record_telemetry | ❌ |
| /api/engines/config/validate | engines.py | validate_configuration | ❌ |
| /api/engines/recommend | engines.py | recommend_engine | ❌ |
| /api/engines/{engine_id}/start | engines.py | start_engine | ❌ |
| /api/engines/{engine_id}/stop | engines.py | stop_engine | ❌ |
| /api/ensemble/multi-engine | ensemble.py | create_multi_engine_ensemble | ❌ |
| /api/errors/export | errors.py | export_error_report | ❌ |
| /api/errors/{error_id}/resolve | errors.py | resolve_error | ❌ |
| /api/eval-abx/sessions/{session_id}/complete | eval_abx.py | complete_session | ❌ |
| /api/eval-abx/start | eval_abx.py | start | ❌ |
| /api/eval-abx/submit | eval_abx.py | submit_result | ❌ |
| /api/experiments/{experiment_id}/complete | experiments.py | complete_experiment | ❌ |
| /api/experiments/{experiment_id}/pause | experiments.py | pause_experiment | ❌ |
| /api/experiments/{experiment_id}/resume | experiments.py | resume_experiment | ❌ |
| /api/experiments/{experiment_id}/start | experiments.py | start_experiment | ❌ |
| /api/face-swap/create | face_swap.py | create_face_swap | ❌ |
| /api/feedback/submit | feedback.py | submit_feedback | ❌ |
| /api/feedback/{feedback_id}/attachments | feedback.py | add_attachment | ❌ |
| /api/feedback/{feedback_id}/respond | feedback.py | add_response | ❌ |
| /api/formant/analyze | formant.py | analyze | ❌ |
| /api/formant/apply | formant.py | apply | ❌ |
| /api/granular/render | granular.py | render | ❌ |
| /api/health/circuit-breakers/{engine_id}/reset | health.py | reset_circuit_breaker | ✅ |
| /api/help/shortcuts | help.py | create_keyboard_shortcut | ❌ |
| /api/help/topics | help.py | create_help_topic | ❌ |
| /api/huggingface-fix/apply | huggingface_fix.py | apply | ✅ |
| /api/image-gen/enhance-face | image_gen.py | enhance_face | ❌ |
| /api/image-gen/generate | image_gen.py | generate_image | ❌ |
| /api/image-gen/upscale | image_gen.py | upscale_image | ❌ |
| /api/image-search/search | image_search.py | search_images | ❌ |
| /api/img-sampler/render | img_sampler.py | render | ❌ |
| /api/instant-cloning/estimate-quality | instant_cloning.py | estimate_quality | ❌ |
| /api/instant-cloning/extract-embedding | instant_cloning.py | extract_embedding | ❌ |
| /api/instant-cloning/preview | instant_cloning.py | instant_preview | ❌ |
| /api/instant-cloning/zero-shot | instant_cloning.py | zero_shot_clone | ❌ |
| /api/integrations/batch/start | integrations.py | start_batch | ❌ |
| /api/integrations/batch/{job_id}/cancel | integrations.py | cancel_batch | ❌ |
| /api/integrations/daw/export | integrations.py | export_to_daw | ❌ |
| /api/integrations/sync/start | integrations.py | start_sync | ❌ |
| /api/integrations/video/export | integrations.py | export_for_video | ❌ |
| /api/integrations/workflows/start | integrations.py | start_workflow | ❌ |
| /api/integrations/workflows/{execution_id}/cancel | integrations.py | cancel_workflow | ❌ |
| /api/jobs/history/clear | jobs.py | clear_job_history | ❌ |
| /api/jobs/{job_id}/cancel | jobs.py | cancel_job | ❌ |
| /api/jobs/{job_id}/pause | jobs.py | pause_job | ❌ |
| /api/jobs/{job_id}/resume | jobs.py | resume_job | ❌ |
| /api/jobs/{job_id}/retry | jobs.py | retry_job | ❌ |
| /api/lexicon/add | lexicon.py | add_lexicon_entry | ❌ |
| /api/lexicon/lexicons | lexicon.py | create_lexicon | ❌ |
| /api/lexicon/lexicons/{lexicon_id}/entries | lexicon.py | create_lexicon_entry | ❌ |
| /api/lexicon/phoneme | lexicon.py | estimate_phonemes | ❌ |
| /api/lexicon/search | lexicon.py | search_lexicon_entries | ❌ |
| /api/library/assets | library.py | create_asset | ❌ |
| /api/library/assets/upload | library.py | upload_asset | ❌ |
| /api/library/folders | library.py | create_folder | ❌ |
| /api/lip-sync/extract-phonemes | lip_sync.py | extract_phonemes | ❌ |
| /api/lip-sync/generate | lip_sync.py | generate_lip_sync | ❌ |
| /api/lip-sync/preview | lip_sync.py | preview_lip_sync | ❌ |
| /api/macros/automation | macros.py | create_track_automation | ✅ |
| /api/macros/automation/curves | macros.py | create_automation_curve | ✅ |
| /api/macros/{macro_id}/execute | macros.py | execute_macro | ✅ |
| /api/macros/{macro_id}/schedule | macros.py | schedule_macro | ❌ |
| /api/marketplace/downloads/{plugin_id}/record | marketplace.py | record_download | ❌ |
| /api/marketplace/publishers | marketplace.py | register_publisher | ❌ |
| /api/marketplace/review-queue/{submission_id}/review | marketplace.py | review_submission | ❌ |
| /api/marketplace/reviews/{plugin_id} | marketplace.py | submit_review | ❌ |
| /api/marketplace/submissions | marketplace.py | submit_plugin | ❌ |
| /api/mix-assistant/analyze | mix_assistant.py | analyze_mix | ❌ |
| /api/mix-assistant/apply | mix_assistant.py | apply_suggestions | ❌ |
| /api/mix-assistant/master/analyze | mix_assistant.py | analyze_mastering | ❌ |
| /api/mix-assistant/master/apply | mix_assistant.py | apply_mastering | ❌ |
| /api/mix-assistant/mix/analyze | mix_assistant.py | analyze_mix_simple | ❌ |
| /api/mix-assistant/mix/apply | mix_assistant.py | apply_mix_suggestions_simple | ❌ |
| /api/mix-assistant/mix/suggest | mix_assistant.py | get_mix_suggestions_simple | ❌ |
| /api/mix-assistant/presets/generate | mix_assistant.py | generate_preset | ❌ |
| /api/mixer/meters/{project_id}/simulate | mixer.py | simulate_meter_updates | ❌ |
| /api/mixer/presets/{project_id} | mixer.py | create_preset | ❌ |
| /api/mixer/presets/{project_id}/{preset_id}/apply | mixer.py | apply_preset | ❌ |
| /api/mixer/state/{project_id}/reset | mixer.py | reset_mixer_state | ❌ |
| /api/mixer/state/{project_id}/returns | mixer.py | create_return | ❌ |
| /api/mixer/state/{project_id}/sends | mixer.py | create_send | ❌ |
| /api/mixer/state/{project_id}/subgroups | mixer.py | create_subgroup | ❌ |
| /api/ml-optimization/explain | ml_optimization.py | explain_model | ❌ |
| /api/ml-optimization/optimize | ml_optimization.py | optimize_hyperparameters | ❌ |
| /api/model-inspect/inspect | model_inspect.py | inspect | ❌ |
| /api/models/experiment | models.py | create_model_experiment | ❌ |
| /api/models/import | models.py | import_model | ❌ |
| /api/models/registry/{engine_id}/activate | models.py | activate_registry_model | ❌ |
| /api/models/registry/{engine_id}/rollback | models.py | rollback_registry_model | ❌ |
| /api/models/{engine}/{model_name}/verify | models.py | verify_model | ❌ |
| /api/monitoring/alerts/{alert_id}/acknowledge | monitoring.py | acknowledge_alert | ❌ |
| /api/monitoring/alerts/{alert_id}/resolve | monitoring.py | resolve_alert | ❌ |
| /api/multi-speaker-dubbing/assign-voices | multi_speaker_dubbing.py | assign_voices | ❌ |
| /api/multi-speaker-dubbing/diarize | multi_speaker_dubbing.py | diarize_speakers | ❌ |
| /api/multi-speaker-dubbing/generate | multi_speaker_dubbing.py | generate_dubbing | ❌ |
| /api/multi-voice-generator/compare | multi_voice_generator.py | compare_voices | ❌ |
| /api/multi-voice-generator/export | multi_voice_generator.py | export_multi_voice | ❌ |
| /api/multi-voice-generator/generate | multi_voice_generator.py | generate_multi_voice | ❌ |
| /api/multi-voice-generator/import | multi_voice_generator.py | import_multi_voice | ❌ |
| /api/multilingual/synthesize | multilingual.py | synthesize_multilingual | ❌ |
| /api/multilingual/translate | multilingual.py | translate_text | ❌ |
| /api/nr/apply | nr.py | apply | ❌ |
| /api/nr/noise-print/create | nr.py | create_noise_print | ❌ |
| /api/orchestrator/cancel/{job_id} | orchestrator.py | cancel_job | ❌ |
| /api/orchestrator/presets | orchestrator.py | save_preset | ❌ |
| /api/orchestrator/run | orchestrator.py | run_orchestration | ❌ |
| /api/pdf/extract-text | pdf.py | extract_text_for_tts | ❌ |
| /api/pdf/read | pdf.py | read_pdf | ❌ |
| /api/pipeline/process | pipeline.py | process_pipeline | ❌ |
| /api/plugin-gallery/install | plugin_gallery.py | install_plugin | ❌ |
| /api/plugin-gallery/installed/{plugin_id}/disable | plugin_gallery.py | disable_plugin | ❌ |
| /api/plugin-gallery/installed/{plugin_id}/enable | plugin_gallery.py | enable_plugin | ❌ |
| /api/plugin-gallery/updates/{plugin_id} | plugin_gallery.py | update_plugin | ❌ |
| /api/plugin-health/metrics/prune | plugin_health.py | prune_old_metrics | ❌ |
| /api/plugins/{plugin_id}/load | plugins.py | load_plugin | ❌ |
| /api/plugins/{plugin_id}/unload | plugins.py | unload_plugin | ❌ |
| /api/plugins/{plugin_id}/wasm/execute | plugins.py | execute_wasm_plugin | ❌ |
| /api/presets/{preset_id}/apply | presets.py | apply_preset | ❌ |
| /api/projects/{project_id}/audio/save | projects.py | save_audio_to_project | ✅ |
| /api/prosody/apply | prosody.py | apply_prosody | ❌ |
| /api/prosody/configs | prosody.py | create_prosody_config | ❌ |
| /api/prosody/phonemes/analyze | prosody.py | analyze_phonemes | ❌ |
| /api/quality-pipelines/engines/{engine_id}/apply | quality_pipelines.py | apply_pipeline | ❌ |
| /api/quality-pipelines/engines/{engine_id}/compare | quality_pipelines.py | compare_pipeline | ❌ |
| /api/quality-pipelines/engines/{engine_id}/preview | quality_pipelines.py | preview_pipeline | ❌ |
| /api/quality/analyze | quality.py | analyze_quality | ❌ |
| /api/quality/analyze-text | quality.py | analyze_text_endpoint | ❌ |
| /api/quality/benchmark | quality.py | run_benchmark | ❌ |
| /api/quality/compare | quality.py | compare_quality | ❌ |
| /api/quality/consistency/record | quality.py | record_quality_metrics | ❌ |
| /api/quality/consistency/standard | quality.py | set_quality_standard | ❌ |
| /api/quality/history | quality.py | store_quality_history | ❌ |
| /api/quality/optimize | quality.py | optimize_quality | ❌ |
| /api/quality/recommend-quality | quality.py | recommend_quality_endpoint | ❌ |
| /api/quality/visualization/anomalies | quality.py | detect_quality_anomalies_endpoint | ❌ |
| /api/quality/visualization/correlations | quality.py | get_quality_correlations | ❌ |
| /api/quality/visualization/export/anomalies | quality.py | export_quality_anomalies | ❌ |
| /api/quality/visualization/export/correlations | quality.py | export_quality_correlations | ❌ |
| /api/quality/visualization/export/heatmap | quality.py | export_quality_heatmap | ❌ |
| /api/quality/visualization/export/insights | quality.py | export_quality_insights | ❌ |
| /api/quality/visualization/heatmap | quality.py | get_quality_heatmap | ❌ |
| /api/quality/visualization/insights | quality.py | get_quality_insights | ❌ |
| /api/quality/visualization/predict | quality.py | predict_quality_endpoint | ❌ |
| /api/realtime-converter/start | realtime_converter.py | start_converter_session | ❌ |
| /api/realtime-converter/{session_id}/pause | realtime_converter.py | pause_converter_session | ❌ |
| /api/realtime-converter/{session_id}/resume | realtime_converter.py | resume_converter_session | ❌ |
| /api/realtime-converter/{session_id}/stop | realtime_converter.py | stop_converter_session | ❌ |
| /api/realtime-visualizer/start | realtime_visualizer.py | start_visualizer_session | ❌ |
| /api/realtime-visualizer/{session_id}/stop | realtime_visualizer.py | stop_visualizer_session | ❌ |
| /api/recording/start | recording.py | start_recording | ❌ |
| /api/recording/{recording_id}/chunk | recording.py | append_audio_chunk | ❌ |
| /api/recording/{recording_id}/stop | recording.py | stop_recording | ❌ |
| /api/repair/clipping | repair.py | clipping | ❌ |
| /api/rvc/convert | rvc.py | convert_voice | ❌ |
| /api/rvc/models/upload | rvc.py | upload_model | ❌ |
| /api/rvc/start | rvc.py | start | ❌ |
| /api/rvc/stop | rvc.py | stop | ❌ |
| /api/safety/scan | safety.py | scan | ❌ |
| /api/scenes/{scene_id}/apply | scenes.py | apply_scene | ❌ |
| /api/scenes/{scene_id}/tracks | scenes.py | add_track_to_scene | ❌ |
| /api/settings/reset | settings.py | reset_settings | ❌ |
| /api/shortcuts/reset-all | shortcuts.py | reset_all_shortcuts | ❌ |
| /api/shortcuts/{shortcut_id}/reset | shortcuts.py | reset_shortcut | ❌ |
| /api/slo/alerts/{alert_id}/acknowledge | slo.py | acknowledge_alert | ❌ |
| /api/slo/export | slo.py | export_slo_status | ❌ |
| /api/slo/record/{metric_name} | slo.py | record_metric | ❌ |
| /api/sonography/generate | sonography.py | generate_sonography | ❌ |
| /api/spatial-audio/apply | spatial_audio.py | apply_spatial_audio | ❌ |
| /api/spatial-audio/binaural | spatial_audio.py | generate_binaural_audio | ❌ |
| /api/spatial-audio/configs | spatial_audio.py | create_spatial_config | ❌ |
| /api/spatial-audio/environment | spatial_audio.py | configure_environment | ❌ |
| /api/spatial-audio/position | spatial_audio.py | set_voice_position | ❌ |
| /api/spatial-audio/preview | spatial_audio.py | preview_spatial_audio | ❌ |
| /api/spatial-audio/process | spatial_audio.py | process_spatial_audio | ❌ |
| /api/spectral/inpaint | spectral.py | inpaint | ❌ |
| /api/ssml/preview | ssml.py | preview_ssml | ❌ |
| /api/ssml/validate | ssml.py | validate_ssml | ❌ |
| /api/style-transfer/presets | style_transfer.py | create_style_preset | ❌ |
| /api/style-transfer/style/analyze | style_transfer.py | analyze_style | ❌ |
| /api/style-transfer/style/extract | style_transfer.py | extract_style | ❌ |
| /api/style-transfer/synthesize/style | style_transfer.py | synthesize_with_style | ❌ |
| /api/style-transfer/transfer | style_transfer.py | create_style_transfer | ❌ |
| /api/tags/merge | tags.py | merge_tags | ❌ |
| /api/tags/{tag_id}/decrement-usage | tags.py | decrement_tag_usage | ❌ |
| /api/tags/{tag_id}/increment-usage | tags.py | increment_tag_usage | ❌ |
| /api/telemetry/reset | telemetry.py | reset_metrics | ❌ |
| /api/templates/{template_id}/apply | templates.py | apply_template | ❌ |
| /api/text-speech-editor/align | text_speech_editor.py | align_transcript | ❌ |
| /api/text-speech-editor/apply | text_speech_editor.py | apply_edits | ❌ |
| /api/text-speech-editor/insert-text | text_speech_editor.py | insert_text | ❌ |
| /api/text-speech-editor/merge | text_speech_editor.py | merge_segments | ❌ |
| /api/text-speech-editor/remove-filler-words | text_speech_editor.py | remove_filler_words | ❌ |
| /api/text-speech-editor/replace-word | text_speech_editor.py | replace_word | ❌ |
| /api/text-speech-editor/session/create | text_speech_editor.py | create_edit_session | ❌ |
| /api/text-speech-editor/sessions | text_speech_editor.py | create_session | ❌ |
| /api/text-speech-editor/sessions/{session_id}/synthesize | text_speech_editor.py | synthesize_session | ❌ |
| /api/timeline/clips | timeline.py | add_clip | ❌ |
| /api/timeline/loop | timeline.py | set_loop | ❌ |
| /api/timeline/playhead | timeline.py | set_playhead | ❌ |
| /api/timeline/redo | timeline.py | redo | ❌ |
| /api/timeline/tracks | timeline.py | add_track | ❌ |
| /api/timeline/undo | timeline.py | undo | ❌ |
| /api/tracing/export | tracing.py | export_traces | ❌ |
| /api/tracks/redo | tracks.py | redo_track_edit | ✅ |
| /api/tracks/undo | tracks.py | undo_track_edit | ✅ |
| /api/tracks/{track_id}/clips | tracks.py | create_clip | ✅ |
| /api/training/cancel/{training_id} | training.py | cancel_training | ❌ |
| /api/training/datasets | training.py | create_dataset | ❌ |
| /api/training/datasets/{dataset_id}/optimize | training.py | optimize_training_data | ❌ |
| /api/training/export | training.py | export_trained_model | ❌ |
| /api/training/hyperparameters/optimize | training.py | optimize_hyperparameters | ❌ |
| /api/training/import | training.py | import_trained_model | ❌ |
| /api/training/start | training.py | start_training | ❌ |
| /api/transcribe/ | transcribe.py | transcribe_audio_route | ❌ |
| /api/translation/projects | translation.py | create_project | ❌ |
| /api/translation/projects/{project_id}/export-subtitles | translation.py | export_subtitles | ❌ |
| /api/translation/projects/{project_id}/transcribe | translation.py | transcribe_project | ❌ |
| /api/translation/projects/{project_id}/translate | translation.py | translate_project | ❌ |
| /api/upscaling/upscale | upscaling.py | upscale_media | ❌ |
| /api/version/negotiate | version.py | negotiate_version | ❌ |
| /api/video-enhance/detect-faces | video_enhance.py | detect_faces | ❌ |
| /api/video-enhance/start | video_enhance.py | start_enhancement | ❌ |
| /api/video-gen/generate | video_gen.py | generate_video | ❌ |
| /api/video-gen/temporal-consistency | video_gen.py | enhance_temporal_consistency | ❌ |
| /api/video-gen/upscale | video_gen.py | upscale_video | ❌ |
| /api/video-gen/voice/convert | video_gen.py | convert_voice | ❌ |
| /api/voice-browser/refresh | voice_browser.py | refresh_catalog | ❌ |
| /api/voice-cloning-wizard/start | voice_cloning_wizard.py | start_wizard | ❌ |
| /api/voice-cloning-wizard/validate-audio | voice_cloning_wizard.py | validate_audio | ❌ |
| /api/voice-cloning-wizard/{job_id}/finalize | voice_cloning_wizard.py | finalize_wizard | ❌ |
| /api/voice-cloning-wizard/{job_id}/process | voice_cloning_wizard.py | process_wizard | ❌ |
| /api/voice-effects/apply | voice_effects.py | apply_voice_effect | ❌ |
| /api/voice-effects/hotkeys | voice_effects.py | set_hotkey | ❌ |
| /api/voice-effects/realtime/start | voice_effects.py | start_realtime_session | ❌ |
| /api/voice-effects/realtime/{session_id}/effect | voice_effects.py | change_realtime_effect | ❌ |
| /api/voice-effects/realtime/{session_id}/stop | voice_effects.py | stop_realtime_session | ❌ |
| /api/voice-morph/apply | voice_morph.py | apply_morph | ❌ |
| /api/voice-morph/configs | voice_morph.py | create_morph_config | ❌ |
| /api/voice-morph/voice/blend | voice_morph.py | blend_voices | ❌ |
| /api/voice-morph/voice/embedding | voice_morph.py | get_voice_embedding | ❌ |
| /api/voice-morph/voice/morph | voice_morph.py | morph_voice | ❌ |
| /api/voice-morph/voice/preview | voice_morph.py | preview_voice | ❌ |
| /api/voice-speech/phonemize | voice_speech.py | phonemize_text | ❌ |
| /api/voice-speech/recognize | voice_speech.py | recognize_speech | ❌ |
| /api/workflows/{workflow_id}/execute | workflows.py | execute_workflow | ❌ |

### PUT (45 endpoints)

| Path | File | Function | Implementation |
|------|------|----------|----------------|
| /api/automation/{curve_id} | automation.py | update_automation_curve | ❌ |
| /api/effects/chains/{chain_id} | effects.py | update_effect_chain | ✅ |
| /api/emotion/preset/{preset_id} | emotion.py | update_preset | ❌ |
| /api/engines/config/defaults/{task_type} | engines.py | set_default_engine | ❌ |
| /api/engines/config/gpu/settings | engines.py | update_gpu_settings | ❌ |
| /api/engines/config/{engine_id} | engines.py | update_engine_config | ❌ |
| /api/experiments/{experiment_id} | experiments.py | update_experiment | ❌ |
| /api/help/shortcuts/{key} | help.py | update_keyboard_shortcut | ❌ |
| /api/help/topics/{topic_id} | help.py | update_help_topic | ❌ |
| /api/lexicon/lexicons/{lexicon_id} | lexicon.py | update_lexicon | ❌ |
| /api/lexicon/lexicons/{lexicon_id}/entries/{word} | lexicon.py | update_lexicon_entry | ❌ |
| /api/lexicon/update | lexicon.py | update_lexicon_entry_simple | ❌ |
| /api/library/assets/{asset_id} | library.py | update_asset | ❌ |
| /api/macros/automation/curves/{curve_id} | macros.py | update_automation_curve | ✅ |
| /api/macros/automation/{curve_id} | macros.py | update_track_automation | ✅ |
| /api/macros/{macro_id} | macros.py | update_macro | ✅ |
| /api/markers/{marker_id} | markers.py | update_marker | ❌ |
| /api/mixer/presets/{project_id}/{preset_id} | mixer.py | update_preset | ❌ |
| /api/mixer/state/{project_id} | mixer.py | update_mixer_state | ❌ |
| /api/mixer/state/{project_id}/channels/{channel_id}/routing | mixer.py | update_channel_routing | ❌ |
| /api/mixer/state/{project_id}/master | mixer.py | update_master | ❌ |
| /api/mixer/state/{project_id}/returns/{return_id} | mixer.py | update_return | ❌ |
| /api/mixer/state/{project_id}/sends/{send_id} | mixer.py | update_send | ❌ |
| /api/mixer/state/{project_id}/subgroups/{subgroup_id} | mixer.py | update_subgroup | ❌ |
| /api/models/{engine}/{model_name}/update-checksum | models.py | update_model_checksum | ❌ |
| /api/plugins/{plugin_id}/config | plugins.py | update_plugin_config | ❌ |
| /api/presets/{preset_id} | presets.py | update_preset | ❌ |
| /api/profiles/{profile_id} | profiles.py | update_profile | ✅ |
| /api/projects/{project_id} | projects.py | update_project | ✅ |
| /api/prosody/configs/{config_id} | prosody.py | update_prosody_config | ❌ |
| /api/scenes/{scene_id} | scenes.py | update_scene | ❌ |
| /api/settings/{category} | settings.py | update_settings_category | ❌ |
| /api/shortcuts/{shortcut_id} | shortcuts.py | update_shortcut | ❌ |
| /api/spatial-audio/configs/{config_id} | spatial_audio.py | update_spatial_config | ❌ |
| /api/spectrogram/config/{audio_id} | spectrogram.py | update_spectrogram_config | ❌ |
| /api/ssml/{document_id} | ssml.py | update_ssml_document | ❌ |
| /api/tags/{tag_id} | tags.py | update_tag | ❌ |
| /api/templates/{template_id} | templates.py | update_template | ❌ |
| /api/text-speech-editor/sessions/{session_id} | text_speech_editor.py | update_session | ❌ |
| /api/tracks/{track_id} | tracks.py | update_track | ✅ |
| /api/tracks/{track_id}/clips/{clip_id} | tracks.py | update_clip | ✅ |
| /api/transcribe/{transcription_id} | transcribe.py | update_transcription | ❌ |
| /api/voice-morph/configs/{config_id} | voice_morph.py | update_morph_config | ❌ |
| /api/waveform/config/{audio_id} | waveform.py | update_waveform_config | ❌ |
| /api/workflows/{workflow_id} | workflows.py | update_workflow | ❌ |

---

## 📁 Route Files Analysis

| File | Endpoints | Violations | Structure Issues |
|------|-----------|------------|------------------|
| _engine_shared.py | 0 | 0 | ⚠️ 3 |
| _persistent_store.py | 0 | 0 | ⚠️ 3 |
| _state.py | 1 | 0 | ⚠️ 3 |
| advanced_settings.py | 2 | 0 | ✅ |
| advanced_spectrogram.py | 6 | 0 | ✅ |
| ai_enhancement.py | 6 | 0 | ✅ |
| ai_production_assistant.py | 5 | 0 | ✅ |
| analytics.py | 7 | 0 | ✅ |
| api_key_manager.py | 2 | 0 | ✅ |
| articulation.py | 1 | 0 | ✅ |
| assistant.py | 6 | 0 | ✅ |
| assistant_run.py | 3 | 0 | ⚠️ 1 |
| audio.py | 9 | 0 | ✅ |
| audio_analysis.py | 6 | 0 | ✅ |
| audio_audit.py | 4 | 0 | ⚠️ 1 |
| auth.py | 6 | 0 | ✅ |
| automation.py | 7 | 0 | ✅ |
| backup.py | 5 | 0 | ✅ |
| batch.py | 11 | 0 | ✅ |
| consent.py | 5 | 0 | ✅ |
| dataset.py | 5 | 0 | ✅ |
| dataset_editor.py | 3 | 0 | ✅ |
| diagnostics.py | 7 | 0 | ✅ |
| drift.py | 3 | 0 | ✅ |
| dubbing.py | 2 | 0 | ✅ |
| effects.py | 9 | 0 | ✅ |
| embedding_explorer.py | 7 | 0 | ✅ |
| emotion.py | 10 | 0 | ✅ |
| emotion_style.py | 3 | 0 | ✅ |
| engine.py | 3 | 0 | ⚠️ 1 |
| engine_audit.py | 4 | 0 | ⚠️ 1 |
| engines.py | 17 | 0 | ✅ |
| ensemble.py | 4 | 0 | ✅ |
| errors.py | 8 | 0 | ✅ |
| eval_abx.py | 5 | 0 | ✅ |
| experiments.py | 9 | 0 | ✅ |
| face_swap.py | 6 | 0 | ✅ |
| feedback.py | 8 | 0 | ✅ |
| formant.py | 2 | 0 | ⚠️ 1 |
| gateway_aliases.py | 0 | 0 | ✅ |
| gpu_status.py | 2 | 0 | ✅ |
| granular.py | 1 | 0 | ⚠️ 1 |
| health.py | 18 | 0 | ⚠️ 1 |
| help.py | 12 | 0 | ✅ |
| huggingface_fix.py | 2 | 0 | ⚠️ 1 |
| image_gen.py | 5 | 0 | ✅ |
| image_search.py | 6 | 0 | ✅ |
| img_sampler.py | 3 | 0 | ⚠️ 1 |
| instant_cloning.py | 6 | 0 | ✅ |
| integrations.py | 14 | 0 | ✅ |
| jobs.py | 10 | 0 | ✅ |
| lexicon.py | 15 | 0 | ✅ |
| library.py | 10 | 0 | ✅ |
| lip_sync.py | 6 | 0 | ✅ |
| macros.py | 17 | 0 | ✅ |
| markers.py | 4 | 0 | ✅ |
| marketplace.py | 10 | 0 | ✅ |
| metrics.py | 0 | 0 | ✅ |
| mix_assistant.py | 11 | 0 | ✅ |
| mixer.py | 22 | 0 | ✅ |
| ml_optimization.py | 3 | 0 | ✅ |
| model_inspect.py | 2 | 0 | ⚠️ 1 |
| models.py | 13 | 0 | ✅ |
| monitoring.py | 9 | 0 | ✅ |
| multi_speaker_dubbing.py | 7 | 0 | ✅ |
| multi_voice_generator.py | 6 | 0 | ✅ |
| multilingual.py | 5 | 0 | ✅ |
| nr.py | 3 | 0 | ⚠️ 1 |
| orchestrator.py | 10 | 0 | ✅ |
| pdf.py | 4 | 0 | ✅ |
| pipeline.py | 3 | 0 | ✅ |
| plugin_gallery.py | 11 | 0 | ✅ |
| plugin_health.py | 13 | 0 | ✅ |
| plugins.py | 11 | 0 | ✅ |
| presets.py | 6 | 0 | ✅ |
| profiles.py | 3 | 0 | ✅ |
| projects.py | 6 | 0 | ✅ |
| prosody.py | 7 | 0 | ✅ |
| quality.py | 29 | 0 | ✅ |
| quality_pipelines.py | 5 | 0 | ✅ |
| realtime_converter.py | 8 | 0 | ✅ |
| realtime_settings.py | 0 | 0 | ✅ |
| realtime_visualizer.py | 4 | 0 | ✅ |
| recording.py | 6 | 0 | ✅ |
| repair.py | 1 | 0 | ✅ |
| rvc.py | 6 | 0 | ✅ |
| safety.py | 2 | 0 | ✅ |
| scenes.py | 6 | 0 | ✅ |
| search.py | 0 | 0 | ✅ |
| settings.py | 4 | 0 | ✅ |
| shortcuts.py | 7 | 0 | ✅ |
| slo.py | 7 | 0 | ✅ |
| sonography.py | 4 | 0 | ✅ |
| spatial_audio.py | 11 | 0 | ✅ |
| spectral.py | 1 | 0 | ✅ |
| spectrogram.py | 6 | 0 | ✅ |
| ssml.py | 5 | 0 | ✅ |
| style_transfer.py | 9 | 0 | ✅ |
| tags.py | 8 | 0 | ✅ |
| telemetry.py | 4 | 0 | ✅ |
| templates.py | 5 | 0 | ✅ |
| text_speech_editor.py | 13 | 0 | ✅ |
| timeline.py | 7 | 0 | ✅ |
| tracing.py | 7 | 0 | ✅ |
| tracks.py | 8 | 0 | ✅ |
| training.py | 15 | 0 | ✅ |
| training_audit.py | 4 | 0 | ⚠️ 1 |
| transcribe.py | 7 | 0 | ✅ |
| translation.py | 10 | 0 | ✅ |
| upscaling.py | 6 | 0 | ✅ |
| version.py | 6 | 0 | ✅ |
| video_edit.py | 1 | 0 | ✅ |
| video_enhance.py | 6 | 0 | ✅ |
| video_gen.py | 7 | 0 | ✅ |
| voice_browser.py | 5 | 0 | ✅ |
| voice_cloning_wizard.py | 6 | 0 | ✅ |
| voice_effects.py | 11 | 0 | ✅ |
| voice_morph.py | 10 | 2 | ✅ |
| voice_speech.py | 4 | 0 | ✅ |
| waveform.py | 5 | 0 | ✅ |
| workflows.py | 4 | 0 | ✅ |

---

## 🔍 Detailed Route File Status

### _engine_shared.py

- **Endpoints:** 0
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No APIRouter definition found
  - FastAPI imports may be missing
  - No response_model parameters found (may be intentional)

### _persistent_store.py

- **Endpoints:** 0
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No APIRouter definition found
  - FastAPI imports may be missing
  - No response_model parameters found (may be intentional)

### _state.py

- **Endpoints:** 1
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No APIRouter definition found
  - FastAPI imports may be missing
  - No response_model parameters found (may be intentional)

### advanced_settings.py

- **Endpoints:** 2
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### advanced_spectrogram.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### ai_enhancement.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### ai_production_assistant.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### analytics.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### api_key_manager.py

- **Endpoints:** 2
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### articulation.py

- **Endpoints:** 1
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### assistant.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### assistant_run.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### audio.py

- **Endpoints:** 9
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### audio_analysis.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### audio_audit.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### auth.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### automation.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### backup.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### batch.py

- **Endpoints:** 11
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### consent.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### dataset.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### dataset_editor.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### diagnostics.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### drift.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### dubbing.py

- **Endpoints:** 2
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### effects.py

- **Endpoints:** 9
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### embedding_explorer.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### emotion.py

- **Endpoints:** 10
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### emotion_style.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### engine.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### engine_audit.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### engines.py

- **Endpoints:** 17
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### ensemble.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### errors.py

- **Endpoints:** 8
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### eval_abx.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### experiments.py

- **Endpoints:** 9
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### face_swap.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### feedback.py

- **Endpoints:** 8
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### formant.py

- **Endpoints:** 2
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### gateway_aliases.py

- **Endpoints:** 0
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### gpu_status.py

- **Endpoints:** 2
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### granular.py

- **Endpoints:** 1
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### health.py

- **Endpoints:** 18
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### help.py

- **Endpoints:** 12
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### huggingface_fix.py

- **Endpoints:** 2
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### image_gen.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### image_search.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### img_sampler.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### instant_cloning.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### integrations.py

- **Endpoints:** 14
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### jobs.py

- **Endpoints:** 10
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### lexicon.py

- **Endpoints:** 15
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### library.py

- **Endpoints:** 10
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### lip_sync.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### macros.py

- **Endpoints:** 17
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### markers.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### marketplace.py

- **Endpoints:** 10
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### metrics.py

- **Endpoints:** 0
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### mix_assistant.py

- **Endpoints:** 11
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### mixer.py

- **Endpoints:** 22
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### ml_optimization.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### model_inspect.py

- **Endpoints:** 2
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### models.py

- **Endpoints:** 13
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### monitoring.py

- **Endpoints:** 9
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### multi_speaker_dubbing.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### multi_voice_generator.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### multilingual.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### nr.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### orchestrator.py

- **Endpoints:** 10
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### pdf.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### pipeline.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### plugin_gallery.py

- **Endpoints:** 11
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### plugin_health.py

- **Endpoints:** 13
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### plugins.py

- **Endpoints:** 11
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### presets.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### profiles.py

- **Endpoints:** 3
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### projects.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### prosody.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### quality.py

- **Endpoints:** 29
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### quality_pipelines.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### realtime_converter.py

- **Endpoints:** 8
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### realtime_settings.py

- **Endpoints:** 0
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### realtime_visualizer.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### recording.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### repair.py

- **Endpoints:** 1
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### rvc.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### safety.py

- **Endpoints:** 2
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### scenes.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### search.py

- **Endpoints:** 0
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### settings.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### shortcuts.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### slo.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### sonography.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### spatial_audio.py

- **Endpoints:** 11
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### spectral.py

- **Endpoints:** 1
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### spectrogram.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### ssml.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### style_transfer.py

- **Endpoints:** 9
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### tags.py

- **Endpoints:** 8
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### telemetry.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### templates.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### text_speech_editor.py

- **Endpoints:** 13
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### timeline.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### tracing.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### tracks.py

- **Endpoints:** 8
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### training.py

- **Endpoints:** 15
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### training_audit.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ⚠️ Issues found
  - No response_model parameters found (may be intentional)

### transcribe.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### translation.py

- **Endpoints:** 10
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### upscaling.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### version.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### video_edit.py

- **Endpoints:** 1
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### video_enhance.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### video_gen.py

- **Endpoints:** 7
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### voice_browser.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### voice_cloning_wizard.py

- **Endpoints:** 6
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### voice_effects.py

- **Endpoints:** 11
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### voice_morph.py

- **Endpoints:** 10
- **Code Quality:** ⚠️ 2 violations found
  - Line 246: Found 'PLACEHOLDER' - # Synthesize with target voice (using placeholder text)
  - Line 884: Found 'PLACEHOLDER' - # If all extraction methods failed, return error instead of placeholder
- **Structure:** ✅ No issues

### voice_speech.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### waveform.py

- **Endpoints:** 5
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues

### workflows.py

- **Endpoints:** 4
- **Code Quality:** ✅ No violations
- **Structure:** ✅ No issues


---

## 📝 Notes

- ✅ = Success
- ❌ = Failed or Not Available
- ⚠️ = Warning or Issue Found
- Code quality violations include TODO, FIXME, placeholders, etc.
- Implementation checks verify functions are not just 'pass' or raise NotImplementedError
