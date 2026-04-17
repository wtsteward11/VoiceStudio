# Slice 7 — Global Search Truth Recovery

**Date:** 2026-04-17  
**Status:** PASS (automated gates); live-backend rows **Skipped** when `127.0.0.1:8000` is down (expected for headless/CI)

---

## 1) Search truth contract (frozen)

### Backend: `GET /api/search`

| Input | Expected |
|-------|----------|
| `q` | Required; **min length 2** → **422** if missing or too short |
| `types` | Optional comma-separated filter |
| `limit` | Optional, default 50, **1–100** |

### Success JSON

| Field | Meaning |
|-------|---------|
| `total_results` | Must equal `len(results)` in current handler |
| `results` | Array of result items with `id`, `type`, `title`, `panel_id`, … |

### ViewModel mapping

| Backend | ViewModel |
|---------|-----------|
| `total_results` | `TotalResults` |
| `results` | `Results` / `FilteredResults` |
| Short query (< 2 chars) | Clears collections, `TotalResults = 0`, **`SelectedResult = null`** (Slice 7 honesty fix) |

### UI empty-state (`GlobalSearchView.xaml.cs`)

`EmptyStatePanel` visible when `TotalResults == 0 && !IsLoading && string.IsNullOrEmpty(ErrorMessage)`.

### Contract guardrail

List/search must use **`GET /api/search?q=...`** only. Do not invent ambiguous path-style URLs for search.

---

## 2) Implementation summary

| Area | Change |
|------|--------|
| Backend tests | `TestSearchHttpContract` in [tests/unit/backend/api/routes/test_search.py](tests/unit/backend/api/routes/test_search.py): HTTP validation (422), response shape, `types=profile` filter; patched in-memory stores to avoid SQLite/asyncio conflicts during ASGI tests |
| Live-backend C# | [GlobalSearchRuntimeLiveBackendTests.cs](src/VoiceStudio.App.Tests/ViewModels/GlobalSearchRuntimeLiveBackendTests.cs): API `total_results` vs `len(results)` vs `SearchClient` vs `GlobalSearchViewModel` |
| ViewModel | [GlobalSearchViewModel.cs](src/VoiceStudio.App/ViewModels/GlobalSearchViewModel.cs): clear `SelectedResult` when there are no hits or query is too short |
| Seam tests | Extended [GlobalSearchViewModelTests.cs](src/VoiceStudio.App.Tests/ViewModels/GlobalSearchViewModelTests.cs), [GlobalSearchViewModelSeamTests.cs](src/VoiceStudio.App.Tests/ViewModels/GlobalSearchViewModelSeamTests.cs) |
| Stub synthesis regression | [SynthesisStubLiveBackendTests.cs](src/VoiceStudio.App.Tests/ViewModels/SynthesisStubLiveBackendTests.cs): **403 → Inconclusive** when backend is not in stub mode (documented expectation) |

---

## 3) Commands executed (this closure run)

```text
python -m pytest tests/unit/backend/api/routes/test_search.py -q
→ 15 passed

dotnet test ... --filter "FullyQualifiedName~GlobalSearch" -v q
→ 17 passed, 2 skipped (live-backend when no server)

dotnet test ... --filter "FullyQualifiedName~EffectChainClientLiveBackendTests|...|SynthesisStubLiveBackendTests|...|LibraryRuntimeLiveBackendTests"
→ 4 passed, 1 skipped (synthesis stub inconclusive when backend not VOICESTUDIO_TEST_MODE=stub)

dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
→ 0 errors
```

---

## 4) Optional UI proof

Screenshots not captured in this agent run. Empty-state honesty is covered by:

- VM `SelectedResult` cleared on zero-hit searches
- Code-behind visibility rule documented above

---

## 5) Regression checklist

- **PASS:** Search route tests (15)
- **PASS:** GlobalSearch unit/seam tests + live-backend class (skipped without server)
- **PASS:** Slice 6 effects live-backend + Library + Profiles (synth stub skipped if not stub backend)
- **PASS:** Solution build clean
