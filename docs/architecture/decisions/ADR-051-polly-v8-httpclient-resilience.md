# ADR-051: Polly v8 HttpClient Resilience for BackendHttpContext

**Status:** Accepted  
**Date:** 2026-04-09  
**Decision Makers:** Engineering (GOV-VOICESTUDIO-GAP022-HTTPCLIENT-POLLY-RESILIENCE-01)

## Context

The WinUI app talked to FastAPI via `BackendClient` and `BackendHttpContext` with a hand-rolled stack: `RetryHelper` + `CircuitBreaker` in `BackendClientHttpPipeline`. That stack had gaps (cancellation token not threaded into retry delays; coarse circuit failure counting for `TaskCanceledException`; `Random` created per jitter sample). Resilience lived **above** `HttpClient`, so a separate NSwag-based path (`BackendClientAdapter`) did not share the same policy.

## Options Considered

1. **Patch hand-rolled `RetryHelper` / pipeline only** — Small diff; does not unify handler-level behavior with other `HttpClient` users; continued maintenance of custom circuit/retry math.
2. **Add `Polly.Core` (v8) and apply a `DelegatingHandler` around the inner handler chain** — Industry-standard semantics; cancellation-aware retries; shared pipeline for all sends on that `HttpClient`; no `IHttpClientFactory` migration in this slice.
3. **`Microsoft.Extensions.Http.Resilience` + `IHttpClientFactory`/`AddHttpClient`** — Aligns with Microsoft extensions; larger DI and lifetime refactor for VoiceStudio’s singleton `BackendHttpContext`.

## Decision

Use **option 2**: add **`Polly.Core` 8.x** (BSD-3-Clause, free) and build a **`ResiliencePipeline<HttpResponseMessage>`** wired through **`ResiliencePipelineDelegatingHandler`** inserted in the existing handler chain (`DegradedModeClearHandler` → resilience → metrics → correlation → inner). Remove application-layer retry/circuit from `BackendClientHttpPipeline` so behavior is not doubled. Keep `RetryHelper`/`CircuitBreaker` types for other code paths (e.g. `BackendTransport`) until a follow-on row consolidates them.

## Consequences

**Positive:** Testable, cancellable retries; circuit state exposed via Polly `CircuitBreakerStateProvider`; consistent policy for every `HttpClient` send on the backend client.

**Negative:** New production NuGet dependency; policy tuning is code-first (constants) until a future row adds config knobs.

## Related

- [GOV_VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP022_HTTPCLIENT_POLLY_RESILIENCE_01_EXECUTION_ROW.md)
- [ADR-048](ADR-048-centralized-request-coordination.md) (request coordination remains separate from transport resilience)
