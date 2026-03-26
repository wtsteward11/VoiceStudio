# PR-11: Effects Extraction — Scope (Frozen)

**Status:** Scoped (2026-03-22)
**Prerequisite:** PR-10 Workflows complete
**Related:** [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)

---

## Objective

Extract all effect chain and preset methods from `IBackendClient`/`BackendClient` into `IEffectChainClient`/`EffectChainClient`. EffectChainClient switches from thin IBackendClient delegation to pipeline ownership (BackendClientHttpPipeline). Same pattern as PR-9 (MacroClient), PR-10 (WorkflowAutomationClient).

---

## Exact Methods Leaving IBackendClient

| Method | Signature | Route |
|--------|-----------|-------|
| GetEffectChainsAsync | `Task<List<EffectChain>> GetEffectChainsAsync(string projectId, CancellationToken ct = default)` | GET /api/effects/chains/{projectId} |
| GetEffectChainAsync | `Task<EffectChain> GetEffectChainAsync(string projectId, string chainId, CancellationToken ct = default)` | GET /api/effects/chains/{projectId}/{chainId} |
| CreateEffectChainAsync | `Task<EffectChain> CreateEffectChainAsync(string projectId, EffectChain chain, CancellationToken ct = default)` | POST /api/effects/chains/{projectId} |
| UpdateEffectChainAsync | `Task<EffectChain> UpdateEffectChainAsync(string projectId, string chainId, EffectChain chain, CancellationToken ct = default)` | PUT /api/effects/chains/{projectId}/{chainId} |
| DeleteEffectChainAsync | `Task<bool> DeleteEffectChainAsync(string projectId, string chainId, CancellationToken ct = default)` | DELETE /api/effects/chains/{projectId}/{chainId} |
| ProcessAudioWithChainAsync | `Task<EffectProcessResponse> ProcessAudioWithChainAsync(string projectId, string chainId, string audioId, string? outputFilename = null, CancellationToken ct = default)` | POST /api/effects/chains/{projectId}/{chainId}/process?audio_id=... |
| GetEffectPresetsAsync | `Task<List<EffectPreset>> GetEffectPresetsAsync(string? effectType = null, CancellationToken ct = default)` | GET /api/effects/presets |

---

## Destination

- **Interface:** `IEffectChainClient` — add `GetEffectChainAsync` (currently missing; BackendClient has it). All other methods already exist.
- **Implementation:** `EffectChainClient` — inject `BackendClientHttpPipeline` instead of IBackendClient; implement all 7 methods via pipeline
- **Pattern:** Same as MacroClient — internal ctor for tests; DI uses `BackendHttpContext.Pipeline`

---

## Call Sites

| Caller | Methods Used | Change |
|--------|--------------|--------|
| EffectsMixerViewModel | GetEffectChainsAsync, CreateEffectChainAsync, UpdateEffectChainAsync, DeleteEffectChainAsync, ApplyEffectChainAsync (uses MixerStateClient), GetEffectPresetsAsync | None (uses IEffectChainClient) |
| CreateEffectChainAction, DeleteEffectChainAction | CreateEffectChainAsync, DeleteEffectChainAsync | None (use IEffectChainClient) |
| EffectChainClient (current) | Delegates all 7 to IBackendClient | Replace with pipeline |
| EffectsMixerViewModelTests | Mocks IBackendClient for GetEffectChainAsync, GetEffectPresetsAsync | Migrate to mock IEffectChainClient |

---

## Retained Exceptions

- None expected. All effect chain/preset traffic routes through IEffectChainClient after extraction.

---

## Proof Requirements

1. Build passes: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. Targeted tests: `dotnet test --filter "FullyQualifiedName~EffectChain|FullyQualifiedName~EffectsMixer|FullyQualifiedName~BackendClientExtractionRegression"`
3. verify.ps1 -Quick: PASS
4. Extraction inventory: Add PR-11 section with proof artifact path
5. Sweep: No effect chain methods on IBackendClient/BackendClient
6. Anti-regression guard: Add `IBackendClient_DoesNotExposeEffectChainMethods`, `BackendClient_DoesNotExposeEffectChainMethods` to BackendClientExtractionRegressionTests
7. Seam test: `GetEffectChainsAsync_ResolvesCorrectPath` in BackendClientTransportPolicyTests

---

## Out-of-Scope

- No Effects UI changes
- No Models bundling
- No changes to EffectsMixerViewModel beyond what DI requires (none; already uses IEffectChainClient)
- No mixer state / IMixerStateClient changes

---

## Migration Steps

1. Add `GetEffectChainAsync` to IEffectChainClient (missing; BackendClient has it; EffectsMixerViewModelTests expects it)
2. EffectChainClient: change ctor from `(IBackendClient)` to `(BackendClientHttpPipeline pipeline)`; implement all 7 methods via pipeline
3. AppServices: register EffectChainClient with `sp.GetRequiredService<BackendHttpContext>().Pipeline`
4. Remove all 7 methods from IBackendClient
5. Remove all 7 methods from BackendClient
6. Update MockBackendClient: remove effect chain method stubs if any
7. Migrate EffectsMixerViewModelTests: mock IEffectChainClient instead of IBackendClient for effect chain tests
8. Add seam test GetEffectChainsAsync_ResolvesCorrectPath
9. Add anti-regression guards for effect chain methods
10. Run proof; update inventory; update STATE.md with actual artifact path
