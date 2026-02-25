# Supply Chain Attestation

**Owner:** Overseer (Role 0)
**Created:** 2026-02-21 (Phase 12 WS2)
**Last Updated:** 2026-02-23
**ADR Reference:** ADR-044 (Supply-Chain Integrity)

## Attestation Statement

VoiceStudio v1.0.2 GA has been assessed for supply-chain integrity with the following controls in place:

### 1. Software Bill of Materials (SBOM)

- **Generator:** `backend/plugins/supply_chain/sbom.py`
- **Coverage:** Python dependencies, engine manifests, plugin catalogs
- **Format:** CycloneDX-compatible JSON
- **Update frequency:** Generated on each release build

### 2. Dependency Integrity

- **Lock file:** `requirements.txt` with pinned versions
- **Hash verification:** pip `--require-hashes` compatible (when hashes present)
- **Audit tool:** `pip-audit` run during Phase 12 WS1
- **Result:** 0 known CVEs as of 2026-02-21 (see `CVE_EXCEPTIONS.md`)

### 3. Plugin Ecosystem Security

- **Manifest schema:** `shared/schemas/plugin-manifest.schema.json` (v6)
- **Signing infrastructure:** `backend/plugins/supply_chain/signer.py` (ready, signing deferred pending key management)
- **Vulnerability scanner:** `backend/plugins/supply_chain/vuln_scanner.py`
- **License checker:** `backend/plugins/supply_chain/license_checker.py`
- **Audit logging:** `backend/plugins/supply_chain/audit.py`
- **Sandbox:** `backend/plugins/sandbox/` (process isolation, resource monitoring, network policy)

### 4. Installer Provenance

- **Builder:** Inno Setup (`installer/VoiceStudio.iss`)
- **Prerequisites:** .NET 8 Runtime, Windows App SDK (`installer/prerequisites.iss`)
- **Distribution:** Direct download (no third-party app stores)
- **Packaging:** Unpackaged EXE (no MSIX) per ADR-010

### 5. Build Reproducibility

- **CI pipelines:** `.github/workflows/` (build, test, release)
- **Verification harness:** `scripts/verify.ps1` (8-stage gate check)
- **Build logs:** `.buildlogs/` directory with binlog archives

## Limitations

- Plugin signing is infrastructure-ready but deferred (no signing keys yet)
- SBOM generation is semi-automated (manual trigger)
- No binary reproducibility guarantee (Inno Setup builds are non-deterministic)

## Related

- [ADR-044: Supply-Chain Integrity](../architecture/decisions/ADR-044-supply-chain-integrity.md)
- [CVE Exceptions](CVE_EXCEPTIONS.md)
- [CHANGELOG](../../CHANGELOG.md)
