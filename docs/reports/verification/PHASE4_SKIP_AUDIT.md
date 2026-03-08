# Phase 4.2: Skipped Tests Audit

**Date**: 2026-03-07
**Plan**: Architect Hardening Plan

## Summary

Skipped tests fall into documented categories. **No backend bugs** identified that require fixes.

## Categories

| Category | Count (approx) | Skip Reason | Action |
|----------|----------------|-------------|--------|
| Engine integration | 50+ | Optional engine not installed (lyrebird, vosk, tortoise, etc.) | Documented; env-dependent |
| Engine adapters | 5 | Optional adapter (emotion_synthesizer, xtts_service_client, etc.) | Documented |
| Library imports | 30+ | Optional lib (pedalboard, gputil, essentia, fairseq, etc.) | Documented |
| E2E batch/workflow | 20+ | Backend not running / connection refused | Use TestClient (Phase 4.1 pattern) |
| Database operations | 15 | DB/transaction tests (SQLite path, migrations) | Documented |
| Lyrebird engine | 10 | Lyrebird not installed | Documented |
| Completion guard | 1 | Specific path/guard condition | Documented |
| Audio utils | 1 | detect_silence (optional) | Documented |

## Conclusion

All skips are for:
- **Environment/model dependencies** (engines, optional libs)
- **Backend availability** (E2E; Phase 4.1 TestClient fallback applies to jobs; batch E2E could use same pattern if needed)
- **Database/infrastructure** (transaction, migration tests)

No backend logic bugs found. Skips are appropriate and documented.
