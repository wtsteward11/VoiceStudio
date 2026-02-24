# ADR-044: Supply-Chain Integrity

**Status:** Accepted
**Date:** 2026-02-21
**Decision Makers:** Overseer (Role 0)
**Phase:** 12 - Strategic Hardening (WS2)

## Context

VoiceStudio distributes plugins, engine adapters, and model artifacts that users install locally. Without supply-chain controls, tampered or vulnerable dependencies could compromise user systems. Phase 12 WS2 required establishing provenance, integrity verification, and vulnerability awareness for the full application and its plugin ecosystem.

## Decision

Implement a layered supply-chain integrity system:

1. **Full-App SBOM**: Generate Software Bill of Materials via `backend/plugins/supply_chain/sbom.py` covering Python dependencies, engine manifests, and plugin catalogs.
2. **Dependency Hashes**: Lock dependency versions with cryptographic hashes in `requirements.txt` (pip `--require-hashes` compatible).
3. **Plugin Signing Infrastructure**: Provide signing and verification via `backend/plugins/supply_chain/signer.py` with `SIGNING_AVAILABLE` capability flag. Actual signing deferred until key management is established.
4. **Vulnerability Scanning**: `backend/plugins/supply_chain/vuln_scanner.py` checks installed packages against known CVE databases.
5. **License Compliance**: `backend/plugins/supply_chain/license_checker.py` validates plugin licenses against an allowlist.
6. **Audit Logging**: `backend/plugins/supply_chain/audit.py` records plugin install, update, and removal events with timestamps and actor identity.
7. **Installer Provenance**: Documented in `docs/governance/SUPPLY_CHAIN_ATTESTATION.md`.

## Consequences

- Users can verify the integrity of installed plugins and dependencies.
- SBOM generation enables compliance with emerging regulatory requirements.
- Plugin signing is infrastructure-ready but deferred (returns `SIGNING_AVAILABLE=False`) until key management decisions are made.
- CVE exceptions are tracked in `docs/governance/CVE_EXCEPTIONS.md`.
- Audit trail provides forensic capability for incident response.

## Related

- ADR-042: Plugin Installer Consolidation
- ADR-040: Dual Plugin Loader
- Phase 12 WS2 deliverables in `.cursor/STATE.md`
