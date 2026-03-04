# Provenance Policy

**Status:** Best-effort (as of 2026-03-01)

## Decision

VoiceStudio uses **best-effort** provenance and usage recording for audio outputs.

- Provenance write failures are logged but do not fail the request.
- Usage stats recording failures are logged but do not fail the request.
- Output audio is returned to the client regardless of provenance/usage success.

## Rationale

- Strict mode (delete output on provenance failure) would degrade UX when storage or security service is temporarily unavailable.
- Best-effort allows synthesis to succeed while still attempting traceability.
- Do not claim "every output is traceable" in documentation; provenance is best-effort.

## Centralized Policy (Milestone 2)

**Single source of truth:** `backend.services.provenance_policy`

- `ProvenancePolicy` enum: `STRICT` | `BEST_EFFORT`
- Configure via env: `VOICESTUDIO_PROVENANCE_POLICY=strict|best_effort` (default: best_effort)
- All provenance/usage failure handling is centralized in `record_artifact_provenance_and_usage`; route handlers do not implement per-callsite policy logic.

## Implementation

- **Registration pipeline:** `AudioRegistry.register(..., model_used="...", duration_seconds=...)` invokes provenance and usage automatically when `model_used` is provided.
- **Direct calls:** Routes that do not use `AudioRegistry.register` call `record_artifact_provenance_and_usage(output_path, model_used=..., duration_seconds=...)` directly.
- **Policy enforcement:** `record_artifact_provenance_and_usage` respects `ProvenancePolicy`; in STRICT mode, failures re-raise; in BEST_EFFORT, log and continue.

## Scope

Applies to all audio-producing endpoints (voice, effects, batch, ensemble, workflows, etc.). Provenance and usage are recorded via the registration pipeline or `record_artifact_provenance_and_usage`.

## Future

If strict traceability becomes a requirement:

1. Set `VOICESTUDIO_PROVENANCE_POLICY=strict` (or add ADR documenting the change).
2. All handlers will automatically fail on provenance/usage errors (no code changes needed).
3. Update this policy accordingly.
