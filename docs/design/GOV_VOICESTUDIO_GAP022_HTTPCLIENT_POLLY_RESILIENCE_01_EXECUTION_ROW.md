# GOV-VOICESTUDIO-GAP022-HTTPCLIENT-POLLY-RESILIENCE-01

**Status:** **Closed** — Implementation complete 2026-04-09. Closure: [VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_LANE_CLOSURE_2026-04-09.md](../reports/verification/VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_LANE_CLOSURE_2026-04-09.md)  
**GAP:** [GAP-022](PROFESSIONAL_GAP_TRACKER.md) — Replace hand-rolled resilience with Polly v8 / `HttpClient` resilience  
**Phase:** 2 (Missing)  
**Row type:** **runtime-affecting**  
**Role:** Core Platform  
**Created:** 2026-04-08  

---

## Problem statement

The desktop client uses `HttpClient` (via `BackendClient` / HTTP pipeline) to reach the local FastAPI backend. Retry, timeout, and circuit behavior should be **explicit, testable, and maintainable** — not ad hoc duplication. [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) tracks this as **GAP-022** (~24h estimate).

## Bounded slice (this lane)

- Introduce **Polly v8** policies (retry / timeout / breaker as appropriate) wired through the existing **`HttpClient`** used by **`BackendClient`** / **`BackendClientHttpPipeline`**.
- Preserve current **observable behavior** for successful paths unless the row’s closure documents an intentional UX change.
- Add or extend **unit / seam tests** that prove policy wiring and key edge cases (without requiring live backend for the core tests).

## Runtime proof requirement

- [x] **Inherited Grade R proof required** — This lane changes **client transport** only, not server synthesis, training, export, or health **product** routes. At closure, cite the most recent **Grade R** artifact (e.g. `artifacts/verify/<ts>/runtime_proof.json` or policy-approved substitute) and confirm it is within the **72h** policy window, **or** rerun `.\scripts\verify.ps1 -RuntimeProof` if inherited proof is stale/missing.

**Cited at closure:** `artifacts/verify/20260408_205616/runtime_proof.json` (2026-04-08; within 72h of closure 2026-04-09). See closure report §4b.

## SLO baseline posture

- **`slo_baseline_freshness`** and **`slo_baselines.json`** remain **advisory** per GAP-015 slice 3. This lane does **not** introduce SLO threshold enforcement. Closure may note advisory freshness only.

## Closure report doctrine (this lane)

At closure, the report MUST explicitly state: **Grade S / I** evidence (tests), **Grade R** inherited vs fresh (see above), and that **SLO** artifacts were **advisory only** for this closure.

**Closure report:** [VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_LANE_CLOSURE_2026-04-09.md](../reports/verification/VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_LANE_CLOSURE_2026-04-09.md).

## Allowlist (initial — refine during implementation)

| Area | Paths |
|------|--------|
| HTTP client / pipeline | `src/VoiceStudio.App/Services/BackendClient.cs`, `BackendClientHttpPipeline.cs`, related `BackendClient.*.cs` partials as needed |
| DI / bootstrap | `src/VoiceStudio.App/Services/AppServiceBootstrapper.cs` (or current canonical registration site for `HttpClient`) |
| Config | `src/VoiceStudio.App/Core/Services/BackendClientConfig.cs` if policy knobs are required |
| Project | `src/VoiceStudio.App/VoiceStudio.App.csproj` (NuGet only with ADR + commit justification) |
| Tests | `src/VoiceStudio.App.Tests/` — targeted tests for pipeline / `BackendClient` resilience |
| Governance | This row, closure report (when implemented), [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md), [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md), `.cursor/STATE.md` |

## Hard OUT

- No changes to FastAPI routes, engine layer, or synthesis/training **business** semantics.
- No new paid or seat-licensed dependencies (Polly is permissive OSS; confirm license at implementation).
- No scope expansion to **GAP-069** (full CI/verify.ps1 in GHA) or **GAP-067** (shell mega-lane).
- No silent degradation of **local-first** / offline expectations: policies must default to safe behavior for localhost.

## Acceptance contract (implementation phase)

- [x] Polly v8 policies applied to the **`HttpClient`** instance(s) used for backend API calls — owned by **`ResiliencePipelineDelegatingHandler`** via **`BackendHttpTransportFactory`** (see ADR-051).
- [x] Retry/timeout/circuit parameters are **configurable or documented constants** with rationale (no magic sleep-only hacks per project standards) — `BackendHttpResiliencePolicies.cs`.
- [x] New or updated **MSTest** coverage for resilience behavior (mock handler / test `HttpMessageHandler`) — `BackendClientTransportPolicyTests.cs`.
- [x] **ADR** accepted for **new production NuGet** — [ADR-051](../../architecture/decisions/ADR-051-polly-v8-httpclient-resilience.md).
- [x] `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` clean; full `dotnet test` for `VoiceStudio.App.Tests` **PASS** at closure seal.
- [x] `.\scripts\verify.ps1 -Quick` **PASS** at closure — `artifacts/verify/20260408_205814/`.
- [x] **Inherited or Fresh Grade R** cited in closure — `artifacts/verify/20260408_205616/runtime_proof.json` (see Runtime proof requirement).
- [x] Tracker **GAP-022** row updated to **Closed** when the bounded slice is fully delivered.

## Rollback

`git revert` of the implementation commit(s) for this lane (NuGet + code + tests + ADR if any).

## Risks

| Risk | Mitigation |
|------|------------|
| Policy hides real outages | Circuit breaker + bounded retries; preserve user-visible errors |
| Test flakiness | Deterministic mock `HttpMessageHandler`; no real network in unit tests |
| NuGet / supply chain | ADR + explicit version pin; `pip`-style hash discipline not applicable to NuGet — follow repo NuGet conventions |

## Changelog

| Date | Change |
|------|--------|
| 2026-04-09 | Lane **Closed**. Implementation + governance seal. Closure: [VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_LANE_CLOSURE_2026-04-09.md](../reports/verification/VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_LANE_CLOSURE_2026-04-09.md). |
