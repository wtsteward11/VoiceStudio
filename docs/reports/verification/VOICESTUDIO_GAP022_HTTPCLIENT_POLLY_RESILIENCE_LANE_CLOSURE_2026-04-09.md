# VOICESTUDIO — GAP-022 HttpClient Polly resilience — Lane closure

**Lane ID:** `GOV-VOICESTUDIO-GAP022-HTTPCLIENT-POLLY-RESILIENCE-01`  
**Tracker:** **GAP-022** — **Closed**  
**Execution row:** [GOV_VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_01_EXECUTION_ROW.md) — **Closed**  
**Closure date:** 2026-04-09  
**Git (proof seal):** Resolve with `git log -1 --format=%H -- docs/reports/verification/VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_LANE_CLOSURE_2026-04-09.md` after merge.

---

## 1. Goal

Replace hand-rolled retry and circuit-breaker logic in the desktop HTTP path with **Polly v8** (`Polly.Core`) policies wired at the **`HttpClient` `DelegatingHandler`** level, so:

- Resilience applies to **all** sends on the shared client (including NSwag-generated adapter paths).
- **Cancellation** propagates through retries and backoff (fixes the prior gap where `RetryHelper` was invoked without a token).
- **Observable behavior** on successful paths is preserved; no intentional UX change beyond correct error mapping from Polly (`BrokenCircuitException` → `BackendUnavailableException`, timeout → `BackendTimeoutException`).

---

## 2. What shipped

| Deliverable | Location |
|-------------|----------|
| ADR (new production NuGet) | [ADR-051-polly-v8-httpclient-resilience.md](../../architecture/decisions/ADR-051-polly-v8-httpclient-resilience.md) |
| NuGet | `Polly.Core` **8.5.0** — `VoiceStudio.App.csproj` |
| Policy builder + constants | `src/VoiceStudio.App/Services/BackendHttpResiliencePolicies.cs` |
| Polly-wrapped handler | `src/VoiceStudio.App/Services/ResiliencePipelineDelegatingHandler.cs` |
| Shared handler chain factory | `src/VoiceStudio.App/Services/BackendHttpTransportFactory.cs` |
| Wiring | `BackendHttpContext.cs`, `BackendClient.cs` (shared factory) |
| Application pipeline simplified (no duplicate retry/circuit) | `BackendClientHttpPipeline.cs` |
| MSTest transport policy coverage | `src/VoiceStudio.App.Tests/Services/BackendClientTransportPolicyTests.cs` |
| Flaky parallel-test harness fix | `TestAppServicesHelper.AppServicesInitializeSyncRoot`; `TranscribeViewModelInlineEditTests` `InstallHarness` / `InstallRetryHarness` outer lock |

**Handler order (outer → inner):** `DegradedModeClearHandler` → Polly (`ResiliencePipelineDelegatingHandler`) → request metrics (when enabled) → correlation id → `HttpClientHandler`.

---

## 3. Policy shape (documented constants)

| Policy | Parameters |
|--------|------------|
| **Retry** | Max **3** retry attempts; exponential backoff with jitter; base **1 s**, max **10 s**; retries on transient HTTP (429, 500–504), `HttpRequestException`, timeout-style `TaskCanceledException` (no user cancel), `TimeoutException`. |
| **Circuit breaker** | Failure ratio **50%**; minimum throughput **5**; sampling **30 s**; break duration **30 s**; same transient predicate as retry. |
| **Timeout** | Per-attempt **30 s** (aligned with `BackendClientConfig.RequestTimeout`). |

`HttpClient.Timeout` remains the hard outer cap. No hedging; no client-side rate limiter in this slice.

---

## 4. Proof seal — Grade S / I (tests + build + Quick)

| Surface | Command | Outcome | Artifact / evidence |
|---------|---------|---------|---------------------|
| **Build** | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **PASS** (exit **0**) | Full solution build at closure. |
| **MSTest** | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **PASS** (exit **0**) | **3196** passed / **274** skipped (full suite at governance seal). |
| **Quick** | `.\scripts\verify.ps1 -Quick` | **PASS** (exit **0**) | `artifacts/verify/20260408_205814/verification_report.md` (authoritative Quick cited with GAP-015 slice 3 seal). |

**Grade S / I:** Transport behavior covered by `BackendClientTransportPolicyTests` and full App.Tests regression; no suppression of failures for closure.

---

## 4b. Grade R — **Inherited** (runtime proof)

Per [GOV_VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_01_EXECUTION_ROW.md) **Runtime proof requirement**, this lane changes **client transport only**, not server synthesis, training, export, or health **product** routes.

**Inherited artifact:** `artifacts/verify/20260408_205616/runtime_proof.json` (from `.\scripts\verify.ps1 -RuntimeProof` on **2026-04-08**).

**Interpretation:** That run reports **FAIL** (honest Grade R) for synthesis (e.g. HTTP **503** when engines unavailable) while other workflow probes may pass — consistent with advisory runtime proof policy. **Closure 2026-04-09** cites this artifact as within the **72h** inherited window; lane scope does not require a fresh `-RuntimeProof` PASS for transport-only closure.

---

## 5. SLO posture — **advisory only**

Per execution row **SLO baseline posture** and GAP-015 slice 3: **`slo_baseline_freshness`** and **`slo_baselines.json`** remain **advisory**. This lane does **not** introduce SLO threshold enforcement or dashboard gating.

---

## 6. Flaky MSTest fix (trustworthy green suite)

**Symptom:** `TranscribeViewModelInlineEditTests.ApplyEdit_Failure_PreservesEditingState` could fail under full parallel load with apply job row stuck **Queued**.

**Cause:** `TestAppServicesHelper.RebuildDefaultProvider()` released its lock before a subsequent `AppServices.Initialize(custom)` in harness install; another test could replace `AppServices` mid-sequence.

**Fix:** Shared **`AppServicesInitializeSyncRoot`** in `TestAppServicesHelper`; outer `lock` around **`InstallHarness`** and **`InstallRetryHarness`** so rebuild + custom provider install are atomic (reentrant with `RebuildDefaultProvider`).

**Residual risk (documented):** Other tests that call `AppServices.Initialize` without the same sync root may still race — follow-up hygiene, not a blocker for this lane closure.

---

## 7. Hard OUT (verified)

- No FastAPI route, engine-layer, or synthesis/training **business** semantics changes in this lane.
- No `IHttpClientFactory` migration in this slice.
- `RetryHelper` / hand-rolled `CircuitBreaker` in `Utilities/` **retained** for other callers (e.g. `BackendTransport`); `BackendClientHttpPipeline` no longer performs application-level retry/circuit (Polly owns transport resilience).

---

## 8. Rollback

`git revert` of the implementation commit set (NuGet, handlers, pipeline, tests, ADR references). Hand-rolled types remain in repo for revert safety.
