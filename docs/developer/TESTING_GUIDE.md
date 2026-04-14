# VoiceStudio Testing Guide

Comprehensive guide for testing VoiceStudio's Python backend, C# frontend, and cross-stack integration.

## Table of Contents

1. [Overview](#overview)
2. [Test Architecture](#test-architecture)
3. [Test Categories](#test-categories)
4. [Running Tests](#running-tests)
5. [Test Fixtures and Factories](#test-fixtures-and-factories)
6. [Writing Tests](#writing-tests)
7. [CI/CD Integration](#cicd-integration)
8. [Coverage Requirements](#coverage-requirements)
9. [Performance Testing](#performance-testing)
10. [Contract Testing](#contract-testing)
11. [Troubleshooting](#troubleshooting)

---

## Overview

VoiceStudio employs a comprehensive testing strategy following the **Test Pyramid** principle:

```
         /\
        /  \       E2E Tests (UI + Backend)
       /----\
      /      \     Integration Tests
     /--------\
    /          \   Unit Tests (Foundation)
   /------------\
```

### Key Principles

- **Fast feedback**: Unit tests run in seconds
- **Isolation**: Tests don't depend on external services unless necessary
- **Reproducibility**: Same tests produce same results every run
- **Coverage**: 95% minimum coverage threshold enforced in CI

---

## Test Architecture

### Directory Structure

```
tests/
├── unit/                      # Fast, isolated unit tests
│   ├── backend/               # Python backend unit tests
│   │   ├── api/               # API route tests
│   │   ├── services/          # Service layer tests
│   │   └── core/              # Core module tests
│   └── __init__.py
│
├── integration/               # Integration tests with real dependencies
│   ├── api/                   # API integration tests
│   ├── backend_frontend/      # Cross-stack integration
│   ├── old_project/           # Legacy system integration
│   └── conftest.py            # Shared fixtures
│
├── e2e/                       # End-to-end tests
│   ├── framework/             # E2E test framework
│   │   ├── base.py            # E2ETestBase class
│   │   ├── page_objects.py    # Page Object Model implementations
│   │   └── locators.py        # Element locators
│   ├── test_wizard_flow.py    # Voice cloning wizard tests
│   ├── test_synthesis_flow.py # Synthesis workflow tests
│   └── test_project_flow.py   # Project management tests
│
├── performance/               # Performance and benchmark tests
│   ├── test_ui_benchmarks.py  # UI rendering performance
│   ├── test_api_benchmarks.py # API latency benchmarks
│   ├── test_engine_benchmarks.py
│   ├── test_memory_profiling.py
│   └── detect_regression.py   # Performance regression detection
│
├── contract/                  # Contract tests
│   ├── test_pact_contracts.py # Consumer-driven contracts
│   ├── test_openapi_contract.py
│   ├── test_engine_manifest.py
│   └── test_shared_schema.py
│
├── quality/                   # Quality verification tests
│   ├── verify_no_placeholders.py
│   └── verify_code_patterns.py
│
├── fixtures/                  # Shared test fixtures
│   ├── factories.py           # Test data factories
│   ├── engines.py             # Mock engine fixtures
│   └── mock_backend.py        # Mock backend for offline testing
│
└── conftest.py                # Global pytest configuration
```

### C# Frontend Tests

```
src/VoiceStudio.App.Tests/
├── ViewModels/                # ViewModel unit tests
├── Services/                  # Service tests
├── Navigation/                # Navigation and panel tests
└── VoiceStudio.App.Tests.csproj
```

---

## Test Categories

### Pytest Markers

| Marker | Description | Example |
|--------|-------------|---------|
| `@pytest.mark.unit` | Fast, isolated tests | Basic function tests |
| `@pytest.mark.integration` | Tests with dependencies | Database, API tests |
| `@pytest.mark.e2e` | End-to-end tests | Full workflow tests |
| `@pytest.mark.slow` | Long-running tests | Large data processing |
| `@pytest.mark.requires_gpu` | GPU-dependent tests | ML inference tests |
| `@pytest.mark.requires_engine` | Engine-dependent tests | TTS/STT tests |
| `@pytest.mark.requires_backend` | Backend-dependent tests | API integration |
| `@pytest.mark.quality` | Quality verification | Code pattern checks |
| `@pytest.mark.performance` | Performance tests | Benchmarks, SLO tests |
| `@pytest.mark.contract` | Contract tests | Schema validation |

### MSTest Categories (C#)

| Category | Description |
|----------|-------------|
| `Smoke` | Quick sanity checks |
| `Unit` | Isolated unit tests |
| `Integration` | Integration tests |
| `Performance` | Performance benchmarks |

---

## Running Tests

### Python Backend Tests

```bash
# Run all unit tests
pytest tests/unit/ -v

# Run with coverage
pytest tests/unit/ -v --cov=backend --cov-report=html

# Run specific markers
pytest -m "unit and not slow" -v

# Run integration tests
pytest tests/integration/ -v

# Run E2E tests (requires backend)
pytest tests/e2e/ -v -m "e2e"

# Run performance tests
pytest tests/performance/ -v --perf-report

# Run contract tests
pytest tests/contract/ -v
```

### C# Frontend Tests

```powershell
# Run all tests
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -v normal

# Run with coverage
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj `
    --collect:"XPlat Code Coverage"

# Run specific categories
dotnet test --filter "TestCategory=Unit"
dotnet test --filter "TestCategory=Smoke"

# Run single test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

### Full Test Suite

```bash
# Backend + Frontend (CI-equivalent)
python -m pytest tests/unit/ tests/integration/ -v --cov=backend
dotnet test VoiceStudio.sln -c Release -p:Platform=x64
```

---

## Test Fixtures and Factories

### Data Factories (`tests/fixtures/factories.py`)

Generate consistent test data:

```python
from tests.fixtures.factories import (
    AudioFactory,
    ProfileFactory,
    ProjectFactory,
    SynthesisJobFactory,
)

# Create test audio
audio = AudioFactory.create_wav_bytes(duration=5.0)
audio_file = AudioFactory.create_wav_file("/tmp/test.wav")

# Create test profile
profile = ProfileFactory.create(name="Test Voice")

# Create test project
project = ProjectFactory.create()

# Create synthesis job
job = SynthesisJobFactory.create_pending(text="Hello")
```

### Engine Fixtures (`tests/fixtures/engines.py`)

Mock engines for testing without real inference:

```python
from tests.fixtures.engines import (
    MockEngineFactory,
    MockEngineService,
)

# Create mock TTS engine
xtts = MockEngineFactory.create_xtts()
audio = xtts.synthesize("Hello world")

# Create mock STT engine
whisper = MockEngineFactory.create_whisper()
result = whisper.transcribe(audio_bytes)

# Create full mock service
service = MockEngineService.create_with_engines()
service.synthesize("Text", engine_id="chatterbox")
```

### Mock Backend (`tests/fixtures/mock_backend.py`)

Simulate backend for offline frontend testing:

```python
from tests.fixtures.mock_backend import MockBackend

backend = MockBackend()

# Make requests
response = backend.get("/health")
response = backend.post("/api/v1/synthesis", {"text": "Hello"})

# Configure failures for error testing
backend.configure_failure("GET", "/api/v1/profiles", 
                          MockResponse.internal_error())

# Assert requests were made
backend.assert_request_made("POST", "/api/v1/synthesis", times=1)
```

### Integration Test Client

```python
from tests.integration.conftest import IntegrationTestClient

async def test_synthesis_endpoint(integration_client):
    response = await integration_client.post(
        "/api/v1/synthesis",
        json={"text": "Test", "engine_id": "xtts_v2"}
    )
    assert response.status_code == 202
    
    # Check recorded requests
    requests = integration_client.get_requests()
    assert len(requests) == 1
```

---

## Writing Tests

### Unit Test Example (Python)

```python
import pytest
from backend.services.engine_service import EngineService

@pytest.mark.unit
class TestEngineService:
    """Unit tests for EngineService."""
    
    def test_list_engines_returns_available_engines(self, mock_engine_registry):
        """Test that list_engines returns only available engines."""
        service = EngineService(registry=mock_engine_registry)
        
        engines = service.list_engines()
        
        assert len(engines) > 0
        assert all(e["status"] == "available" for e in engines)
    
    def test_get_engine_raises_for_unknown_engine(self):
        """Test that get_engine raises ValueError for unknown engine."""
        service = EngineService()
        
        with pytest.raises(ValueError, match="Engine not found"):
            service.get_engine("nonexistent")
```

### Integration Test Example (Python)

```python
import pytest
from httpx import AsyncClient

@pytest.mark.integration
@pytest.mark.requires_backend
class TestSynthesisAPI:
    """Integration tests for synthesis API."""
    
    @pytest.fixture
    async def client(self, app):
        async with AsyncClient(app=app, base_url="http://test") as client:
            yield client
    
    async def test_synthesis_creates_job(self, client):
        """Test that synthesis endpoint creates a job."""
        response = await client.post(
            "/api/v1/synthesis",
            json={"text": "Hello", "engine_id": "xtts_v2"}
        )
        
        assert response.status_code == 202
        data = response.json()
        assert "job_id" in data
        assert data["status"] == "pending"
```

### E2E Test Example

```python
import pytest
from tests.e2e.framework.base import E2ETestBase

@pytest.mark.e2e
@pytest.mark.requires_app
class TestVoiceCloningWizard(E2ETestBase):
    """E2E tests for voice cloning wizard."""
    
    def test_complete_wizard_flow(self):
        """Test complete voice cloning wizard from start to finish."""
        # Navigate to wizard
        self.navigate_to_panel("VoiceCloningWizardView")
        
        # Upload reference audio
        self.wizard_page.upload_audio("tests/fixtures/reference.wav")
        self.wizard_page.click_next()
        
        # Enter voice name
        self.wizard_page.enter_voice_name("Test Voice")
        self.wizard_page.click_next()
        
        # Start cloning
        self.wizard_page.click_start_cloning()
        
        # Wait for completion
        assert self.wizard_page.wait_for_completion(timeout=60)
        assert self.wizard_page.get_success_message() is not None
```

### Unit Test Example (C#)

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

[TestClass]
public class SynthesisViewModelTests
{
    private Mock<IBackendClient> _mockBackend;
    private SynthesisViewModel _viewModel;
    
    [TestInitialize]
    public void Setup()
    {
        _mockBackend = new Mock<IBackendClient>();
        _viewModel = new SynthesisViewModel(_mockBackend.Object);
    }
    
    [TestMethod]
    [TestCategory("Unit")]
    public async Task SynthesizeCommand_WithValidText_CallsBackend()
    {
        // Arrange
        _viewModel.Text = "Hello world";
        _mockBackend
            .Setup(x => x.SynthesizeAsync(It.IsAny<SynthesisRequest>()))
            .ReturnsAsync(new SynthesisResponse { JobId = "123" });
        
        // Act
        await _viewModel.SynthesizeCommand.ExecuteAsync(null);
        
        // Assert
        _mockBackend.Verify(
            x => x.SynthesizeAsync(It.Is<SynthesisRequest>(r => r.Text == "Hello world")),
            Times.Once);
    }
}
```

---

## CI/CD Integration

### GitHub Actions Workflow

The test workflow (`.github/workflows/test.yml`) runs:

1. **test-backend**: Python unit and integration tests with coverage
2. **test-frontend**: C# unit tests with coverage
3. **coverage-gate**: Enforces 95% coverage threshold
4. **contract-tests**: Schema and contract validation
5. **performance-tests**: Benchmarks (on release branches)
6. **e2e-full-app**: Full E2E tests (manual trigger)

### Windows verify-harness (checkpoint + resume)

The workflow [`.github/workflows/verify-harness.yml`](../.github/workflows/verify-harness.yml) runs on **`windows-latest`** and is the authoritative CI reproduction of **`scripts/verify.ps1`** Quick mode and (on `workflow_dispatch` with `run_full_chain` or on weekly schedule) **checkpoint + resume** (`-StopAfterStage "C# Unit Tests - Other"` then `-ResumeFrom "Python Unit Tests"`). It uploads **`artifacts/verify/`** and **`.buildlogs/verification/last_run.json`** as workflow artifacts.

**When to use:** After changing the verification harness, failure-path smoke scripts, or CI templates; or run the weekly scheduled job for drift detection. Trigger manually: **Actions → Verify Harness (Checkpoint + Resume) → Run workflow**.

**Discipline:** See [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md) §8. Contract tests: `tests/unit/test_verify_harness_contract.py`.

### Required Checks

| Job | Required | Blocking |
|-----|----------|----------|
| test-backend | Yes | Yes |
| test-frontend | Yes | Yes |
| coverage-gate | Yes | Yes |
| contract-tests | No | No |
| performance-tests | No | No |

---

## Coverage Requirements

### Thresholds

| Metric | Minimum | Target |
|--------|---------|--------|
| Line Coverage | 95% | 98% |
| Branch Coverage | 90% | 95% |

### Configuration

Coverage is configured in:
- `.coveragerc` - Python coverage settings
- `pytest.ini` - Pytest coverage integration

### Enforcement

```bash
# Check coverage locally
pytest --cov=backend --cov-report=term-missing
coverage report --fail-under=95

# Generate HTML report
coverage html
open htmlcov/index.html
```

### Excluding Lines

```python
# pragma: no cover - Exclude specific lines
if __name__ == "__main__":  # pragma: no cover
    main()
```

---

## Performance Testing

### Benchmarks

```python
from tests.performance.helpers import PerformanceTimer

def test_api_latency():
    """Test API response time meets SLO."""
    with PerformanceTimer() as timer:
        response = client.get("/api/v1/health")
    
    assert timer.elapsed_ms < 100  # SLO: 100ms
```

### SLO Targets

| Metric | Target | Max |
|--------|--------|-----|
| Health endpoint | 10ms | 50ms |
| Profile list | 50ms | 200ms |
| Synthesis start | 100ms | 500ms |
| Panel render | 100ms | 500ms |

### Memory Profiling

```python
from tests.performance.memory import MemoryProfiler

def test_no_memory_leak():
    """Test that operation doesn't leak memory."""
    profiler = MemoryProfiler()
    
    for _ in range(100):
        do_operation()
        profiler.snapshot()
    
    assert not profiler.detect_leak(threshold_mb=50)
```

### Regression Detection

```bash
# Run regression detection
python tests/performance/detect_regression.py \
    --baseline .buildlogs/performance/baseline.json \
    --current .buildlogs/performance/current.json
```

---

## Contract Testing

### Pact Consumer Tests

```python
from tests.contract.test_pact_contracts import Contract, Interaction

def test_synthesis_contract():
    """Test synthesis contract between frontend and backend."""
    contract = Contract(
        consumer="frontend",
        provider="backend",
        interactions=[
            Interaction(
                description="Create synthesis job",
                request={"method": "POST", "path": "/api/v1/synthesis"},
                response={"status": 202, "body": {"job_id": "string"}}
            )
        ]
    )
    
    verifier = ContractVerifier(client)
    assert verifier.verify(contract)
```

### Schema Validation

```python
@pytest.mark.contract
def test_openapi_schema_valid(openapi_schema):
    """Test OpenAPI schema is valid."""
    assert "openapi" in openapi_schema
    assert "paths" in openapi_schema
```

### Engine Manifest Validation

```python
@pytest.mark.contract
def test_engine_manifest_valid(manifest_path):
    """Test engine manifest is valid."""
    manifest = load_manifest(manifest_path)
    
    assert "id" in manifest
    assert "capabilities" in manifest
    assert "contract" in manifest
```

---

## Troubleshooting

### Common Issues

#### Tests Can't Find Modules

```bash
# Ensure PYTHONPATH is set
export PYTHONPATH="${PYTHONPATH}:$(pwd)"

# Or run with python -m
python -m pytest tests/
```

#### E2E Tests Fail to Start App

1. Check WinAppDriver is running
2. Verify app path is correct
3. Enable Developer Mode on Windows

```powershell
# Start WinAppDriver
Start-Process "C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"
```

#### Coverage Below Threshold

1. Identify uncovered files: `coverage html`
2. Write tests for uncovered code
3. Use `# pragma: no cover` sparingly

#### Flaky Tests

1. Check for timing issues
2. Use proper waits/retries
3. Isolate test dependencies

### Debug Mode

```bash
# Run with verbose output
pytest -vvs tests/unit/

# Drop into debugger on failure
pytest --pdb tests/unit/

# Run specific test
pytest tests/unit/test_file.py::TestClass::test_method -vvs
```

### Getting Help

- Check existing tests for patterns
- Review CI logs for failures
- Consult team for complex scenarios

---

## Quick Reference

### Commands

```bash
# Unit tests
pytest tests/unit/ -v

# Integration tests
pytest tests/integration/ -v

# E2E tests
pytest tests/e2e/ -v

# Coverage
pytest --cov=backend --cov-report=html

# Performance
pytest tests/performance/ -v --perf-report

# Contracts
pytest tests/contract/ -v

# C# tests
dotnet test src/VoiceStudio.App.Tests/ -v normal
```

### Markers

```python
@pytest.mark.unit          # Fast, isolated
@pytest.mark.integration   # With dependencies
@pytest.mark.e2e          # End-to-end
@pytest.mark.slow         # Long-running
@pytest.mark.requires_gpu # GPU needed
@pytest.mark.contract     # Contract tests
@pytest.mark.performance  # Benchmarks
```

### Fixtures

```python
# Data factories
AudioFactory, ProfileFactory, ProjectFactory

# Mock engines
MockEngineFactory, MockEngineService

# Mock backend
MockBackend, MockResponse, MockRequest

# Integration
IntegrationTestClient, E2ETestBase
```
