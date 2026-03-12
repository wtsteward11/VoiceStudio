# Error Handling Guide

> **Version**: 1.0.0  
> **Last Updated**: 2026-02-04  
> **Status**: Active

## Overview

VoiceStudio implements a unified error handling system across all layers (UI, Backend, Engines). This guide documents the error envelope format, error propagation patterns, and best practices.

## Unified Error Envelope

All errors across the system use a standardized envelope format. The canonical schema is `shared/schemas/error-envelope.schema.json`. Backend implementation: `backend/api/v3/models.ErrorDetail`.

```json
{
  "code": "INVALID_INPUT",
  "message": "Human-readable error message",
  "field": "text",
  "details": { "constraint": "min_length" },
  "severity": "error",
  "recovery_suggestion": "Retry the operation"
}
```

Within v3 API responses, errors appear in the `errors` array of `StandardResponse`:

```json
{
  "status": "error",
  "message": "Validation failed",
  "errors": [
    {
      "code": "REQUIRED_FIELD",
      "message": "Name is required",
      "field": "name",
      "severity": "error"
    }
  ],
  "meta": { "request_id": "...", "timestamp": "..." }
}
```

### Error Codes

| Code Range | Category | Examples |
|------------|----------|----------|
| E001-E099 | Validation | Invalid input, missing required field |
| E100-E199 | Authentication | Unauthorized, session expired |
| E200-E299 | Engine | Engine not available, inference failed |
| E300-E399 | Audio | Invalid format, processing failed |
| E400-E499 | Storage | File not found, disk full |
| E500-E599 | System | Internal error, service unavailable |

### Severity Levels

- **error**: Critical failure, operation cannot continue
- **warning**: Non-critical issue, operation may continue
- **info**: Informational, no action required

## Backend Error Handling

### FastAPI Exception Handlers

```python
from fastapi import HTTPException
from pydantic import BaseModel

class ErrorDetail(BaseModel):
    code: str
    message: str
    details: dict = {}
    severity: str = "error"
    recoverable: bool = False

@app.exception_handler(HTTPException)
async def http_exception_handler(request, exc):
    return JSONResponse(
        status_code=exc.status_code,
        content={"error": ErrorDetail(
            code=f"E{exc.status_code}",
            message=exc.detail,
            recoverable=exc.status_code < 500
        ).dict()}
    )
```

### Service Layer

```python
class EngineService:
    async def synthesize(self, request):
        try:
            result = await self._engine.run(request)
            return result
        except EngineNotReadyError as e:
            raise HTTPException(
                status_code=503,
                detail=ErrorDetail(
                    code="E201",
                    message="Engine not ready",
                    details={"engine_id": e.engine_id},
                    recoverable=True,
                    suggested_action="Wait for engine initialization"
                ).dict()
            )
```

## Frontend Error Handling

### Unified Error Models

The frontend uses two primary models for standardized error handling that align with the backend's `StandardErrorResponse`:

**GatewayError** (`VoiceStudio.Core.Gateways.GatewayResult.cs`):

Used by the Gateway pattern for structured error responses:

```csharp
public sealed class GatewayError
{
    public string Code { get; }              // Maps to backend error_code
    public string Message { get; }           // Maps to backend message
    public int? StatusCode { get; }          // HTTP status code
    public bool IsRetryable { get; }         // Whether retry is possible
    public string? RecoverySuggestion { get; } // Maps to backend recovery_suggestion
    public JsonElement? Details { get; }     // Maps to backend details
    public string? RequestId { get; }        // Maps to backend request_id
    public string? Timestamp { get; }        // Maps to backend timestamp
    public string? Path { get; }             // Maps to backend path
}
```

**BackendException** (`VoiceStudio.Core.Exceptions.BackendException.cs`):

Exception hierarchy for backend errors:

```csharp
public class BackendException : Exception
{
    public int? StatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public bool IsRetryable { get; set; }
    public string? RecoverySuggestion { get; set; }
    public string? RequestId { get; set; }
    public JsonElement? Details { get; set; }
    public string? Timestamp { get; set; }   // ISO 8601 format
    public string? Path { get; set; }        // API endpoint path
}
```

Subclasses: `BackendValidationException`, `BackendAuthenticationException`, `BackendNotFoundException`, `BackendServerException`, `BackendUnavailableException`, `BackendTimeoutException`.

### IErrorCoordinator

The `IErrorCoordinator` service provides centralized error handling:

```csharp
public interface IErrorCoordinator
{
    Task HandleErrorAsync(Exception ex, string context, ErrorSeverity severity, bool showDialog);
    void ClearError();
    string? CurrentError { get; }
    bool HasError { get; }
    event Action<ErrorInfo>? ErrorOccurred;
    event Action? ErrorCleared;
}
```

### ViewModel Pattern

```csharp
public class VoiceSynthesisViewModel : BaseViewModel
{
    private readonly IErrorCoordinator _errorCoordinator;

    public async Task SynthesizeAsync()
    {
        try
        {
            await _backendClient.SynthesizeAsync(request);
        }
        catch (Exception ex)
        {
            await _errorCoordinator.HandleErrorAsync(
                ex, 
                "VoiceSynthesis.Synthesize",
                showDialog: true
            );
        }
    }
}
```

### Error Dialog Service

```csharp
public interface IErrorDialogService
{
    Task ShowErrorAsync(Exception exception, string? title = null, string? context = null);
    Task<bool> ShowRetryDialogAsync(Exception exception, string operation);
}
```

## Engine Layer Error Handling

### BaseEngine Pattern

```python
class BaseEngine:
    async def run(self, request):
        try:
            self._validate_request(request)
            result = await self._execute(request)
            return result
        except ValidationError as e:
            raise EngineValidationError(
                code="E301",
                message=str(e),
                engine_id=self.id
            )
        except Exception as e:
            logger.error(f"Engine error: {e}", exc_info=True)
            raise EngineExecutionError(
                code="E302",
                message="Engine execution failed",
                details={"original_error": str(e)}
            )
```

## Error Logging

### Structured Logging

All errors are logged with structured context:

```python
logger.error(
    "Operation failed",
    extra={
        "error_code": "E201",
        "operation": "synthesis",
        "engine_id": "xtts_v2",
        "duration_ms": 1500,
        "user_context": {"session_id": "..."}
    }
)
```

### Audit Trail

Critical errors are logged to the audit system:

```python
from app.core.audit import AuditLogger

audit_logger = AuditLogger()
audit_logger.log_error(
    code="E201",
    message="Engine initialization failed",
    severity="error",
    context={"engine_id": "xtts_v2"}
)
```

## Best Practices

1. **Always use error codes** - Never throw raw exceptions without codes
2. **Include context** - Provide enough detail for debugging
3. **Set recoverability** - Help UI decide retry behavior
4. **Log before throwing** - Ensure errors are captured
5. **Use appropriate severity** - Reserve "error" for critical failures

## Related Documentation

- [Error Handling Patterns](ERROR_HANDLING_PATTERNS.md)
- [Logging Standards](../governance/LOGGING_STANDARDS.md)
- [Audit System](../governance/AUDIT_SYSTEM.md)
