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

| Size | 325196 bytes (317.6 KiB) |
| RIFF header | 52 49 46 46 = RIFF / WAVE |
| Body | binary audio — not a JSON error body; does not start with `{` |

## Library Evidence

HTTP 201 library asset; audio_id 71d3a9ea-d865-4a7e-84e1-c58e5c29424f

## Timeline Evidence

timeline revision 34→37; clip_id ab874ef0-4933-4617-8b29-c544ff5a06d2; POST /api/timeline/tracks

## Durability Evidence

Durability non-claim: durability check not requested.

## Explicit Non-Claims

- not operator proof
- not runtime FULL PASS
- not durability proof unless restart durability is explicitly verified above
