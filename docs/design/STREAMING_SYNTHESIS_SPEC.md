# Streaming Synthesis Specification

**Status**: Implemented (v1.0.2) -- WebSocket endpoint `/api/voice/synthesize/stream`, `StreamingAudioPlayer.cs`, `StreamingPipeline`
**Priority**: High
**Estimated Effort**: High
**Dependencies**: Engine layer, WebSocket infrastructure, Audio pipeline

## 1. Executive Summary

This specification defines the architecture for real-time streaming synthesis in VoiceStudio, enabling immediate audio output as text is being processed. This reduces perceived latency for long-form content and enables interactive voice applications.

## 2. Goals and Non-Goals

### 2.1 Goals

- **Immediate audio playback** - Start playing audio within 200ms of synthesis initiation
- **Progressive rendering** - Display waveform as audio chunks arrive
- **Graceful degradation** - Fall back to batch synthesis if streaming unavailable
- **Engine abstraction** - Support streaming from multiple engine backends
- **Interruption support** - Allow stopping mid-stream without resource leaks

### 2.2 Non-Goals

- Real-time voice conversion (separate feature)
- Live microphone input processing (handled by Real-Time Converter)
- Sub-50ms latency (interactive voice applications require dedicated infrastructure)

## 3. Architecture Overview

### 3.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        WinUI Frontend                            │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐  │
│  │ Synthesis   │───▶│ Streaming   │───▶│ WaveformRenderer    │  │
│  │ Panel       │    │ AudioPlayer │    │ (Progressive)       │  │
│  └─────────────┘    └─────────────┘    └─────────────────────┘  │
│         │                  ▲                                     │
│         ▼                  │                                     │
│  ┌─────────────┐    ┌─────────────┐                             │
│  │ ViewModel   │───▶│ AudioBuffer │                             │
│  │ Command     │    │ Manager     │                             │
│  └─────────────┘    └─────────────┘                             │
└─────────────────────────────────────────────────────────────────┘
         │ HTTP + WebSocket
         ▼
┌─────────────────────────────────────────────────────────────────┐
│                       FastAPI Backend                            │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐  │
│  │ /stream/    │───▶│ Streaming   │───▶│ Chunk Encoder       │  │
│  │ synthesize  │    │ Orchestrator│    │ (WAV/Opus/PCM)      │  │
│  └─────────────┘    └─────────────┘    └─────────────────────┘  │
│                            │                                     │
│                            ▼                                     │
│                     ┌─────────────┐                             │
│                     │ Engine Pool │                             │
│                     │ (Streaming) │                             │
│                     └─────────────┘                             │
└─────────────────────────────────────────────────────────────────┘
         │ IPC / Subprocess
         ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Engine Layer                                │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐  │
│  │ XTTS        │    │ Coqui       │    │ Future Engines      │  │
│  │ (Streaming) │    │ (Streaming) │    │ (Streaming Support) │  │
│  └─────────────┘    └─────────────┘    └─────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Data Flow

1. **User initiates synthesis** with text and voice profile
2. **Frontend establishes WebSocket** to `/ws/stream/synthesize`
3. **Backend chunks text** into sentence/phrase segments
4. **Engine generates audio** for each chunk incrementally
5. **Backend encodes and streams** audio chunks via WebSocket
6. **Frontend buffers and plays** audio with progressive waveform

## 4. API Specification

### 4.1 REST Endpoint (Initiation)

```http
POST /api/v1/stream/synthesize/init
Content-Type: application/json

{
  "text": "Long text content to synthesize...",
  "voice_profile_id": "uuid",
  "engine": "xtts",
  "options": {
    "chunk_size": "sentence",  // "sentence", "phrase", "word"
    "audio_format": "pcm_s16le",  // "pcm_s16le", "opus", "wav"
    "sample_rate": 22050,
    "channels": 1
  }
}

Response:
{
  "stream_id": "uuid",
  "websocket_url": "/ws/stream/{stream_id}",
  "estimated_chunks": 15,
  "estimated_duration_ms": 45000
}
```

### 4.2 WebSocket Protocol

```typescript
// Client -> Server
interface StreamCommand {
  type: "start" | "pause" | "resume" | "stop" | "seek";
  payload?: {
    chunk_index?: number;  // For seek
  };
}

// Server -> Client
interface StreamMessage {
  type: "audio_chunk" | "progress" | "metadata" | "complete" | "error";
  
  // For audio_chunk
  chunk_index?: number;
  chunk_data?: string;  // Base64 encoded audio
  chunk_duration_ms?: number;
  
  // For progress
  progress?: number;  // 0-100
  current_chunk?: number;
  total_chunks?: number;
  
  // For metadata
  total_duration_ms?: number;
  sample_rate?: number;
  
  // For error
  error_code?: string;
  error_message?: string;
}
```

### 4.3 Engine Protocol Extension

```python
# app/core/engines/protocols.py

class StreamingSynthesisProtocol(Protocol):
    """Protocol for engines that support streaming synthesis."""
    
    async def synthesize_stream(
        self,
        text: str,
        voice_id: str,
        chunk_callback: Callable[[bytes, int], Awaitable[None]],
        **options
    ) -> StreamingResult:
        """
        Stream audio generation with callbacks per chunk.
        
        Args:
            text: Text to synthesize
            voice_id: Voice profile identifier
            chunk_callback: Async callback receiving (audio_bytes, chunk_index)
            **options: Engine-specific options
            
        Returns:
            StreamingResult with final metadata
        """
        ...
    
    @property
    def supports_streaming(self) -> bool:
        """Whether this engine supports streaming synthesis."""
        ...
    
    @property
    def min_chunk_size(self) -> int:
        """Minimum number of characters per chunk for this engine."""
        ...
```

## 5. Frontend Implementation

### 5.1 StreamingAudioPlayer Service

```csharp
// src/VoiceStudio.App/Services/StreamingAudioPlayer.cs

public interface IStreamingAudioPlayer
{
    event EventHandler<AudioChunkReceivedEventArgs>? ChunkReceived;
    event EventHandler<StreamingProgressEventArgs>? ProgressChanged;
    event EventHandler<StreamingCompletedEventArgs>? Completed;
    event EventHandler<StreamingErrorEventArgs>? Error;
    
    Task<bool> StartStreamingAsync(StreamingSynthesisRequest request);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    
    bool IsStreaming { get; }
    double BufferProgress { get; }  // 0-1, how much is buffered ahead
}

public class StreamingAudioPlayer : IStreamingAudioPlayer
{
    private readonly IBackendClient _backendClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ConcurrentQueue<AudioChunk> _buffer;
    private WebSocketClient? _webSocket;
    
    // Buffer management
    private const int MIN_BUFFER_CHUNKS = 3;  // Start playing after 3 chunks
    private const int MAX_BUFFER_CHUNKS = 20; // Backpressure threshold
    
    // ...
}
```

### 5.2 Progressive Waveform Renderer

```csharp
// src/VoiceStudio.App/Controls/StreamingWaveformRenderer.cs

public sealed class StreamingWaveformRenderer : Control
{
    private readonly List<float> _samples = new();
    private readonly object _samplesLock = new();
    
    public void AppendChunk(float[] samples)
    {
        lock (_samplesLock)
        {
            _samples.AddRange(samples);
        }
        Invalidate();  // Trigger redraw
    }
    
    public void Clear()
    {
        lock (_samplesLock)
        {
            _samples.Clear();
        }
        Invalidate();
    }
    
    // Render logic draws only visible portion
    // with smooth scrolling as new data arrives
}
```

### 5.3 ViewModel Integration

```csharp
// In VoiceSynthesisViewModel

public IAsyncRelayCommand StreamSynthesizeCommand { get; }

private async Task StreamSynthesizeAsync(CancellationToken ct)
{
    try
    {
        IsStreaming = true;
        StreamProgress = 0;
        
        var request = new StreamingSynthesisRequest
        {
            Text = InputText,
            VoiceProfileId = SelectedProfile.Id,
            Engine = SelectedEngine.Id
        };
        
        var success = await _streamingPlayer.StartStreamingAsync(request);
        
        if (!success)
        {
            // Fall back to batch synthesis
            await SynthesizeAsync(ct);
        }
    }
    catch (OperationCanceledException)
    {
        await _streamingPlayer.StopAsync();
    }
    finally
    {
        IsStreaming = false;
    }
}
```

## 6. Backend Implementation

### 6.1 Streaming Orchestrator

```python
# backend/services/streaming_orchestrator.py

class StreamingOrchestrator:
    """Manages streaming synthesis sessions."""
    
    def __init__(self, engine_pool: EnginePool):
        self.engine_pool = engine_pool
        self.active_streams: Dict[str, StreamingSession] = {}
    
    async def start_stream(
        self,
        stream_id: str,
        request: StreamingSynthesisRequest,
        websocket: WebSocket
    ) -> None:
        """Start a streaming synthesis session."""
        
        # Get streaming-capable engine
        engine = await self.engine_pool.acquire_streaming(request.engine)
        
        if not engine.supports_streaming:
            # Fall back to chunked batch synthesis
            await self._fallback_chunked(stream_id, request, websocket)
            return
        
        session = StreamingSession(
            stream_id=stream_id,
            engine=engine,
            websocket=websocket
        )
        self.active_streams[stream_id] = session
        
        try:
            # Chunk text for streaming
            chunks = self._chunk_text(request.text, request.options.chunk_size)
            
            for i, chunk in enumerate(chunks):
                if session.is_cancelled:
                    break
                    
                # Stream this chunk
                await engine.synthesize_stream(
                    text=chunk,
                    voice_id=request.voice_profile_id,
                    chunk_callback=lambda audio, idx: self._send_chunk(
                        websocket, audio, i * 100 + idx
                    )
                )
                
                # Send progress
                await websocket.send_json({
                    "type": "progress",
                    "current_chunk": i + 1,
                    "total_chunks": len(chunks),
                    "progress": int((i + 1) / len(chunks) * 100)
                })
            
            await websocket.send_json({"type": "complete"})
            
        finally:
            await self.engine_pool.release(engine)
            del self.active_streams[stream_id]
```

### 6.2 WebSocket Route

```python
# backend/api/routes/stream_synthesis.py

@router.websocket("/ws/stream/{stream_id}")
async def stream_synthesis_websocket(
    websocket: WebSocket,
    stream_id: str,
    orchestrator: StreamingOrchestrator = Depends(get_orchestrator)
):
    await websocket.accept()
    
    try:
        # Get pending stream request
        request = await get_pending_stream_request(stream_id)
        
        if not request:
            await websocket.send_json({
                "type": "error",
                "error_code": "STREAM_NOT_FOUND",
                "error_message": "Stream session not found or expired"
            })
            return
        
        # Start streaming
        await orchestrator.start_stream(stream_id, request, websocket)
        
    except WebSocketDisconnect:
        await orchestrator.cancel_stream(stream_id)
    except Exception as e:
        await websocket.send_json({
            "type": "error",
            "error_code": "STREAM_ERROR",
            "error_message": str(e)
        })
```

## 7. Engine Support Matrix

| Engine | Streaming Support | Min Chunk | Latency (first chunk) |
|--------|-------------------|-----------|----------------------|
| XTTS v2 | Native | 20 chars | ~150ms |
| Coqui TTS | Native | 15 chars | ~100ms |
| RVC | Post-process only | N/A | N/A |
| Bark | Chunked batch | 50 chars | ~500ms |
| Piper | Native | 10 chars | ~50ms |

## 8. Performance Requirements

| Metric | Target | Maximum |
|--------|--------|---------|
| First chunk latency | <200ms | 500ms |
| Chunk delivery interval | <100ms | 200ms |
| Audio buffer underrun rate | <0.1% | 1% |
| Memory overhead per stream | <50MB | 100MB |
| Concurrent streams | 4 | 8 |

## 9. Implementation Phases

### Phase 1: Core Infrastructure
- [ ] WebSocket endpoint for streaming
- [ ] StreamingOrchestrator service
- [ ] Basic frontend audio player with buffering
- [ ] XTTS streaming integration

### Phase 2: UI Integration
- [ ] Progressive waveform renderer
- [ ] Streaming controls (pause/resume/stop)
- [ ] Buffer progress indicator
- [ ] Fallback to batch synthesis

### Phase 3: Optimization
- [ ] Opus encoding for reduced bandwidth
- [ ] Adaptive chunk sizing
- [ ] Parallel chunk generation
- [ ] Client-side audio processing

### Phase 4: Multi-Engine Support
- [ ] Coqui TTS streaming
- [ ] Piper streaming
- [ ] Chunked batch fallback for non-streaming engines

## 10. Error Handling

### 10.1 Recoverable Errors

| Error | Recovery Strategy |
|-------|------------------|
| Chunk generation timeout | Retry with smaller chunk |
| WebSocket disconnect | Attempt reconnect, resume from last chunk |
| Buffer underrun | Pause playback, wait for buffer fill |
| Engine overload | Queue request, notify user of delay |

### 10.2 Non-Recoverable Errors

| Error | User Action |
|-------|-------------|
| Engine crash | Fall back to batch synthesis |
| Invalid voice profile | Show error, prompt for profile selection |
| Text too long | Suggest breaking into segments |

## 11. Security Considerations

- **Rate limiting**: Max 4 concurrent streams per user
- **Resource limits**: 5 minute max stream duration
- **Authentication**: WebSocket upgrade requires valid session
- **Input validation**: Text length and content validation before streaming

## 12. Related Documents

- [Engine Protocol Specification](ENGINE_CONFIG_SYSTEM.md)
- [WebSocket Architecture (ADR-007)](../architecture/decisions/ADR-007-websocket-realtime.md)
- [Audio Pipeline Design](RUNTIME_ENGINE_SYSTEM.md)

---

**Last Updated**: 2026-02-09
**Author**: VoiceStudio Development Team
