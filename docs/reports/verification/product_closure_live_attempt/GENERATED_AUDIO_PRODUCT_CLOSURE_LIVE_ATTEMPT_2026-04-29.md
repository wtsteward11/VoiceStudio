<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Voice Synthesis Real-Engine Proof (Harness)

**Classification: REAL_ENGINE**

## Engine Mode

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

## Audio Artifact

| Size | 307276 bytes (300.1 KiB) |
| RIFF header | 52 49 46 46 = RIFF / WAVE |
| Body | binary audio — not a JSON error body; does not start with `{` |

## Library Evidence

HTTP 201 library asset; audio_id 125c45b0-51b5-4c8a-a2cc-a6e4243b3a36

## Timeline Evidence

timeline revision 38→41; clip_id 9578880d-2e4d-4142-ae6b-3be1510ceb7f; POST /api/timeline/tracks

## Durability Evidence

Durability non-claim: durability check not requested.

## Explicit Non-Claims

- not operator proof
- not runtime FULL PASS
- not durability proof unless restart durability is explicitly verified above
