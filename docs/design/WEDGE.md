# VoiceStudio Content Creator Wedge

This document defines the **wedge** for VoiceStudio: the target user, primary workflow, success metric, and explicit exclusions. It is the filter for every product and prioritization decision.

## Target User

**Content creators** — podcasters, YouTubers, course creators — who record their own voice and need consistent, broadcast-quality output without a sound engineer.

## Primary Workflow

1. **Record or import** raw audio
2. **Clean** — noise reduction, leveling, de-essing
3. **Clone/match voice** for consistency (same voice across episodes or segments)
4. **Export** broadcast-ready (loudness targets, format)

**Wedge workflow (short):**  
Record/import raw audio → clean (noise, leveling, de-essing) → clone/match voice for consistency → export broadcast-ready (loudness, format).

## Success Metric

**Time from raw recording to broadcast-ready export < 5 minutes** for a 10-minute episode segment.

## Tier 1 Engines (Content Creator Wedge)

For the content-creator wedge, Tier 1 engines are:

- **XTTS v2** — voice cloning and multi-language TTS
- **Piper** — fast local TTS (no GPU required)
- **Whisper / Whisper.cpp** — speech-to-text (transcription)
- **Audio enhancement pipeline** — noise reduction, leveling, loudness normalization

These engines are designated `support_tier: "tier1_supported"` in their manifests and are the default recommendations for the wedge workflow.

## Explicit Exclusions (Not in v1)

The following are **out of scope** for the initial wedge:

- Multi-speaker dubbing
- Real-time voice changing
- Video generation
- Image generation
- Game character batch generation

## Decision Filter

When evaluating a feature or change, ask:

1. Does it help a content creator get from raw audio to broadcast-ready in under 5 minutes?
2. Does it support the chain: record/import → clean → clone/match → export?
3. If not, is it a Tier 2 / experimental capability that does not block the wedge?

If the answer to (1) and (2) is no and (3) is no, the work is out of scope for the wedge.
