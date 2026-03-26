# BackendClient Extraction Stop Criteria

**Purpose:** Define when NOT to extract methods from `IBackendClient` / `BackendClient`. Prevents "purity theater" extraction that increases fragmentation without meaningful coupling reduction.  
**Date:** 2026-03-22  
**Related:** [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md), [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md)

---

## Mandatory Stop Criteria

Do **not** extract a method cluster if any of the following apply:

### 1. Leverage Threshold

**Rule:** Do not extract clusters with fewer than **3 distinct call sites** if extraction adds a new client without reducing coupling meaningfully.

**Rationale:** A new `IXxxClient` + `XxxClient` increases surface area (two new types, DI registration, test doubles). If only 1–2 callers exist and they are stable, the fragmentation cost exceeds the benefit.

**Exception:** If a thin client already exists and the migration is "complete the pipeline ownership" (no new types), this criterion does not apply.

---

### 2. Fragmentation Cost

**Rule:** Do not extract if it increases client count without reducing monolith size by at least **5%** of remaining methods.

**Rationale:** Extracting 2 methods from a 90-method monolith (2.2% reduction) while adding a new client type is poor leverage. Prefer clusters that remove 5+ methods per extraction.

**Calculation:** `(methods removed / total IBackendClient Task methods) >= 0.05`

---

### 3. Sparse Callers

**Rule:** Do not extract if callers are already sparse and stable (e.g., admin-only, rarely used, single-panel).

**Rationale:** Extraction is justified when it clarifies ownership or reduces blast radius. If the cluster has one stable caller and no planned expansion, the status quo may be acceptable.

**Assessment:** If the only caller is a low-traffic settings/admin panel and no other domain touches it, defer extraction.

---

### 4. DTO Glue

**Rule:** Do not extract if the seam is mostly DTO marshaling with no meaningful ownership gain.

**Rationale:** A client that merely forwards `PostAsync<TRequest, TResponse>(endpoint, request)` to `BackendClient.PostAsync` adds no ownership boundary. The caller still depends on the same DTOs and endpoint knowledge.

**Assessment:** If the candidate client would be a one-line pass-through for each method, the extraction may not be worth the DI/complexity unless it enables future domain logic.

---

### 5. Cross-Cutting

**Rule:** Do not extract if the cluster is used across many unrelated domains.

**Rationale:** `SendRequestAsync`, `GetAsync`, `PostAsync`, `PutAsync` are generic HTTP helpers. Extracting them would require every caller to use a different abstraction, increasing churn without domain clarity.

**Keep on IBackendClient:** Generic helpers, MCP `SendMcpOperationAsync`, and any method that serves as a general-purpose transport.

---

## Optional Deferral Criteria

Consider deferring (but not hard-stop) if:

### 6. High DTO Churn

If extraction would require introducing 5+ new DTOs or significant shared model changes, assess whether the benefit justifies the migration cost.

### 7. Test Surface Gap

If no unit/integration tests exist for the cluster, extraction risk is higher. Add tests or run a targeted proof pass before extracting.

### 8. Unclear Ownership

If the cluster straddles multiple bounded contexts (e.g., "quality" + "benchmarking" + "dashboard"), scope a narrow sub-slice first. Do not extract a fuzzy bucket.

---

## Decision Checklist

Before starting an extraction, verify:

- [ ] At least 3 call sites OR thin client already exists
- [ ] Extraction removes >= 5% of remaining methods OR cluster is >= 5 methods
- [ ] Callers are not already sparse and stable
- [ ] Seam is not pure DTO glue
- [ ] Cluster is not cross-cutting (generic transport)
- [ ] Scope is explicit (exact methods, exact callers, exact destination client)

If any checkbox fails, document the exception and obtain explicit approval before proceeding.

---

## Integration

These criteria are referenced by:

- PR-13 Slice Selection plan
- BACKENDCLIENT_REMAINDER_INVENTORY.md (Verdict column)
- Future extraction scope docs (PR-N_XXX_SCOPE.md)
