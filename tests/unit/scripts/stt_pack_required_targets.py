"""Pytest paths and script fragments for ``stt_hardening_regress.ps1`` (Tasks 78–79).

Keeps ``test_stt_hardening_regress_pack.py`` and
``test_stt_hardening_regress_summary_schema.py`` aligned with the script.
"""

from __future__ import annotations

# Order matches ``$pytestArgs`` in ``scripts/stt_hardening_regress.ps1``.
# Tests use set equality on paths.
STT_PACK_PYTEST_PATHS: tuple[str, ...] = (
    "tests/unit/core/engines/test_router_stt_policy.py",
    "tests/unit/core/engines/test_whisper_cpp_engine.py",
    "tests/unit/backend/services/test_model_preflight.py",
    "tests/unit/backend/services/test_preflight_registry.py",
    "tests/unit/backend/api/routes/test_health.py::TestDetailedHealth::test_preflight_check",
    "tests/unit/scripts/test_generate_engine_truth.py",
    "tests/unit/scripts/test_truth_doc_markdown_links.py",
    "tests/unit/scripts/test_truth_session_verify_date_alignment.py",
    "tests/unit/scripts/test_engine_truth_overrides_references.py",
    "tests/unit/scripts/test_stt_hardening_regress_pack.py",
    "tests/unit/scripts/test_state_ledger_contract.py",
    "tests/unit/scripts/test_engine_truth_verify_artifact_alignment.py",
)

STT_PACK_SCRIPT_FRAGMENTS: tuple[str, ...] = (
    "stt_hardening_regress_summary",
    "test_stt_hardening_regress_summary_schema.py",
    "generate_engine_truth.py",
)
