using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Streaming audio player with WebSocket support, progressive playback,
    /// and buffer management for real-time audio synthesis.
    /// </summary>
    public class StreamingAudioPlayer : IDisposable
    {
        private const int DefaultBufferSizeMs = 500;
        private const int MinBufferSizeMs = 100;
        private const int MaxBufferSizeMs = 5000;
        private const int DefaultSampleRate = 24000;
        private const int DefaultChannels = 1;

        private readonly ConcurrentQueue<AudioChunk> _audioBuffer;
        private readonly SemaphoreSlim _bufferSemaphore;
        private readonly object _lockObject = new();

        private NAudio.Wave.BufferedWaveProvider? _bufferedWaveProvider;
        private NAudio.Wave.WaveOutEvent? _waveOut;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _streamingCts;
        private Task? _receiverTask;
        private Task? _playbackTask;

        private int _sampleRate = DefaultSampleRate;
        private int _channels = DefaultChannels;
        private float _volume = 1.0f;
        private bool _disposed;
        private bool _isStreaming;
        private bool _isPlaying;
        private int _bufferSizeMs = DefaultBufferSizeMs;
        private int _receivedChunks;
        private int _playedChunks;

        /// <summary>
        /// Gets whether streaming is in progress.
        /// </summary>
        public bool IsStreaming
        {
            get { lock (_lockObject) { return _isStreaming; } }
            private set { lock (_lockObject) { _isStreaming = value; } }
        }

        /// <summary>
        /// Gets whether audio is currently playing.
        /// </summary>
        public bool IsPlaying
        {
            get { lock (_lockObject) { return _isPlaying; } }
            private set { lock (_lockObject) { _isPlaying = value; } }
        }

        /// <summary>
        /// Gets or sets the playback volume (0.0 to 1.0).
        /// </summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0f, 1.0f);
                if (_waveOut != null)
                {
                    _waveOut.Volume = _volume;
                }
            }
        }

        /// <summary>
        /// Gets or sets the buffer size in milliseconds.
        /// </summary>
        public int BufferSizeMs
        {
            get => _bufferSizeMs;
            set => _bufferSizeMs = Math.Clamp(value, MinBufferSizeMs, MaxBufferSizeMs);
        }

        /// <summary>
        /// Gets the number of chunks received from the stream.
        /// </summary>
        public int ReceivedChunks => _receivedChunks;

        /// <summary>
        /// Gets the number of chunks that have been played.
        /// </summary>
        public int PlayedChunks => _playedChunks;

        /// <summary>
        /// Gets the number of chunks currently in the buffer.
        /// </summary>
        public int BufferedChunks => _audioBuffer.Count;

        /// <summary>
        /// Raised when streaming starts.
        /// </summary>
        public event EventHandler? StreamingStarted;

        /// <summary>
        /// Raised when streaming stops.
        /// </summary>
        public event EventHandler? StreamingStopped;

        /// <summary>
        /// Raised when an audio chunk is received.
        /// </summary>
        public event EventHandler<AudioChunkReceivedEventArgs>? ChunkReceived;

        /// <summary>
        /// Raised when an error occurs.
        /// </summary>
        public event EventHandler<StreamingErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// Raised when synthesis is complete.
        /// </summary>
        public event EventHandler<SynthesisCompleteEventArgs>? SynthesisComplete;

        public StreamingAudioPlayer()
        {
            _audioBuffer = new ConcurrentQueue<AudioChunk>();
            _bufferSemaphore = new SemaphoreSlim(0);
        }

        /// <summary>
        /// Connects to a WebSocket endpoint and starts streaming audio.
        /// </summary>
        /// <param name="websocketUrl">WebSocket URL for streaming.</param>
        /// <param name="synthesisRequest">JSON request to send after connecting.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task StartStreamingAsync(
            string websocketUrl,
            object synthesisRequest,
            CancellationToken cancellationToken = default)
        {
            if (IsStreaming)
            {
                throw new InvalidOperationException("Streaming is already in progress.");
            }

            try
            {
                _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Connect WebSocket
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(websocketUrl), _streamingCts.Token);

                // Reset state
                _receivedChunks = 0;
                _playedChunks = 0;
                while (_audioBuffer.TryDequeue(out _)) { }

                // Initialize audio output
                InitializeAudioOutput();

                IsStreaming = true;
                StreamingStarted?.Invoke(this, EventArgs.Empty);

                // Send synthesis request
                var requestJson = JsonSerializer.Serialize(synthesisRequest);
                var requestBytes = Encoding.UTF8.GetBytes(requestJson);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(requestBytes),
                    WebSocketMessageType.Text,
                    true,
                    _streamingCts.Token);

                // Start receiver and playback tasks
                _receiverTask = Task.Run(() => ReceiveLoopAsync(_streamingCts.Token), _streamingCts.Token);
                _playbackTask = Task.Run(() => PlaybackLoopAsync(_streamingCts.Token), _streamingCts.Token);
            }
            catch (Exception ex)
            {
                await CleanupAsync();
                ErrorOccurred?.Invoke(this, new StreamingErrorEventArgs(ex.Message, ex));
                throw;
            }
        }

        /// <summary>
        /// Stops the current streaming session.
        /// </summary>
        public async Task StopStreamingAsync()
        {
            if (!IsStreaming)
            {
                return;
            }

            try
            {
                // Send stop message
                if (_webSocket?.State == WebSocketState.Open)
                {
                    var stopMessage = JsonSerializer.Serialize(new { type = "stop" });
                    var stopBytes = Encoding.UTF8.GetBytes(stopMessage);
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(stopBytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Error sending stop message: {ex.Message}", "StreamingAudioPlayer");
            }

            await CleanupAsync();
        }

        private void InitializeAudioOutput()
        {
            lock (_lockObject)
            {
                // Create buffered wave provider with format: float32, mono, 24kHz
                var waveFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, _channels);
                _bufferedWaveProvider = new NAudio.Wave.BufferedWaveProvider(waveFormat)
                {
                    BufferLength = _sampleRate * 4 * (_bufferSizeMs * 2 / 1000), // 2x buffer for safety
                    DiscardOnBufferOverflow = true,
                };

                _waveOut = new NAudio.Wave.WaveOutEvent
                {
                    Volume = _volume,
                };
                _waveOut.Init(_bufferedWaveProvider);
                _waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024]; // 64KB receive buffer

            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessMessageAsync(json);
                    }
                }
            }
            // ALLOWED: empty catch - OperationCanceledException expected during normal cancellation flow
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[StreamingAudioPlayer] Receiver loop cancelled");
            }
            catch (WebSocketException ex)
            {
                ErrorOccurred?.Invoke(this, new StreamingErrorEventArgs($"WebSocket error: {ex.Message}", ex));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new StreamingErrorEventArgs($"Receive error: {ex.Message}", ex));
            }
        }

        private async Task ProcessMessageAsync(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeElement))
                {
                    return;
                }

                var messageType = typeElement.GetString();

                switch (messageType)
                {
                    case "start":
                        // Streaming has started on server
                        IsPlaying = true;
                        lock (_lockObject)
                        {
                            _waveOut?.Play();
                        }
                        break;

                    case "audio_chunk":
                        await ProcessAudioChunkAsync(root);
                        break;

                    case "complete":
                        ProcessComplete(root);
                        break;

                    case "error":
                        var errorMessage = root.TryGetProperty("message", out var msgEl)
                            ? msgEl.GetString() ?? "Unknown error"
                            : "Unknown error";
                        ErrorOccurred?.Invoke(this, new StreamingErrorEventArgs(errorMessage, null));
                        break;

                    case "warning":
                        // Log warning but continue
                        var warningMessage = root.TryGetProperty("message", out var warnEl)
                            ? warnEl.GetString()
                            : "Unknown warning";
                        ErrorLogger.LogWarning(warningMessage ?? "Unknown warning", "StreamingAudioPlayer");
                        break;

                    case "pong":
                        // Keepalive response, ignore
                        break;
                }
            }
            catch (JsonException ex)
            {
                ErrorLogger.LogWarning($"Failed to parse message: {ex.Message}", "StreamingAudioPlayer");
            }
        }

        private Task ProcessAudioChunkAsync(JsonElement root)
        {
            // Extract chunk data
            if (!root.TryGetProperty("data", out var dataElement))
            {
                return Task.CompletedTask;
            }

            var base64Data = dataElement.GetString();
            if (string.IsNullOrEmpty(base64Data))
            {
                return Task.CompletedTask;
            }

            var audioBytes = Convert.FromBase64String(base64Data);
            var chunkIndex = root.TryGetProperty("chunk_index", out var idxEl) ? idxEl.GetInt32() : _receivedChunks;
            var sampleRate = root.TryGetProperty("sample_rate", out var srEl) ? srEl.GetInt32() : _sampleRate;

            // Update sample rate if different
            if (sampleRate != _sampleRate && _receivedChunks == 0)
            {
                _sampleRate = sampleRate;
                // Reinitialize audio output with new sample rate
                InitializeAudioOutput();
            }

            // Create chunk and add to buffer
            var chunk = new AudioChunk
            {
                Index = chunkIndex,
                AudioData = audioBytes,
                SampleRate = sampleRate,
                ReceivedAt = DateTime.UtcNow,
            };

            _audioBuffer.Enqueue(chunk);
            Interlocked.Increment(ref _receivedChunks);
            _bufferSemaphore.Release();

            ChunkReceived?.Invoke(this, new AudioChunkReceivedEventArgs(chunkIndex, audioBytes.Length, sampleRate));

            return Task.CompletedTask;
        }

        private void ProcessComplete(JsonElement root)
        {
            var totalChunks = root.TryGetProperty("total_chunks", out var tcEl) ? tcEl.GetInt32() : _receivedChunks;
            var duration = root.TryGetProperty("duration", out var durEl) ? durEl.GetDouble() : 0.0;
            var engine = root.TryGetProperty("engine", out var engEl) ? engEl.GetString() : "unknown";

            SynthesisComplete?.Invoke(this, new SynthesisCompleteEventArgs(totalChunks, duration, engine ?? "unknown"));
        }

        private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Wait for audio chunk
                    await _bufferSemaphore.WaitAsync(cancellationToken);

                    if (_audioBuffer.TryDequeue(out var chunk))
                    {
                        // Add audio data to wave provider
                        lock (_lockObject)
                        {
                            _bufferedWaveProvider?.AddSamples(chunk.AudioData, 0, chunk.AudioData.Length);
                        }

                        Interlocked.Increment(ref _playedChunks);
                    }
                }
            }
            // ALLOWED: empty catch - OperationCanceledException expected during normal cancellation flow
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[StreamingAudioPlayer] Playback loop cancelled");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new StreamingErrorEventArgs($"Playback error: {ex.Message}", ex));
            }
        }

        private void WaveOut_PlaybackStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                ErrorOccurred?.Invoke(this, new StreamingErrorEventArgs($"Playback stopped with error: {e.Exception.Message}", e.Exception));
            }

            IsPlaying = false;
        }

        private async Task CleanupAsync()
        {
            IsStreaming = false;
            IsPlaying = false;

            _streamingCts?.Cancel();

            // Wait for tasks to complete - log errors but continue cleanup
            try
            {
                if (_receiverTask != null)
                {
                    await _receiverTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            catch (TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine("[StreamingAudioPlayer] Receiver task did not complete within timeout during stop");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingAudioPlayer] Error waiting for receiver task: {ex.Message}");
            }

            try
            {
                if (_playbackTask != null)
                {
                    await _playbackTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            catch (TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine("[StreamingAudioPlayer] Playback task did not complete within timeout during stop");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingAudioPlayer] Error waiting for playback task: {ex.Message}");
            }

            // Close WebSocket
            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopped", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StreamingAudioPlayer] Error closing WebSocket: {ex.Message}");
                }
            }

            // Stop audio
            lock (_lockObject)
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;
                _bufferedWaveProvider = null;
            }

            _webSocket?.Dispose();
            _webSocket = null;
            _streamingCts?.Dispose();
            _streamingCts = null;

            StreamingStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _ = Task.Run(async () => await CleanupAsync());
            _bufferSemaphore.Dispose();
        }

        /// <summary>
        /// Checks if streaming synthesis is available for a specific engine.
        /// C.2 Enhancement: Capability check before streaming.
        /// </summary>
        /// <param name="baseUrl">Backend API base URL.</param>
        /// <param name="engineId">Engine ID to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Streaming capability information.</returns>
        public static async Task<StreamingCapability> CheckStreamingCapabilityAsync(
            string baseUrl,
            string engineId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                var response = await httpClient.GetAsync(
                    $"{baseUrl}/api/voice/streaming/capabilities/{engineId}",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return new StreamingCapability
                    {
                        EngineId = engineId,
                        SupportsStreaming = false,
                        SupportsBatch = false,
                        RecommendedMode = "batch",
                        Error = $"HTTP {(int)response.StatusCode}"
                    };
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new StreamingCapability
                {
                    EngineId = engineId,
                    SupportsStreaming = root.TryGetProperty("supports_streaming", out var ss) && ss.GetBoolean(),
                    SupportsBatch = root.TryGetProperty("supports_batch", out var sb) && sb.GetBoolean(),
                    RecommendedMode = root.TryGetProperty("recommended_mode", out var rm) ? rm.GetString() ?? "batch" : "batch",
                    FallbackMode = root.TryGetProperty("fallback_mode", out var fm) ? fm.GetString() : null
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingAudioPlayer] Capability check failed: {ex.Message}");
                return new StreamingCapability
                {
                    EngineId = engineId,
                    SupportsStreaming = false,
                    SupportsBatch = true, // Assume batch works
                    RecommendedMode = "batch",
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Gets all streaming capabilities from the backend.
        /// C.2 Enhancement: Full capability discovery.
        /// </summary>
        public static async Task<StreamingCapabilities> GetStreamingCapabilitiesAsync(
            string baseUrl,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                var response = await httpClient.GetAsync(
                    $"{baseUrl}/api/voice/streaming/capabilities",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return new StreamingCapabilities { WebSocketEndpoint = null };
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var capabilities = new StreamingCapabilities
                {
                    WebSocketEndpoint = root.TryGetProperty("websocket_endpoint", out var ep) ? ep.GetString() : null,
                    TargetLatencyMs = root.TryGetProperty("target_latency_ms", out var lat) ? lat.GetInt32() : 200,
                    ChunkSizeSamples = root.TryGetProperty("chunk_size_samples", out var cs) ? cs.GetInt32() : 4800,
                };

                if (root.TryGetProperty("streaming_engines", out var engines) && engines.ValueKind == JsonValueKind.Array)
                {
                    foreach (var engine in engines.EnumerateArray())
                    {
                        if (engine.GetString() is string engineId)
                        {
                            capabilities.StreamingEngines.Add(engineId);
                        }
                    }
                }

                return capabilities;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingAudioPlayer] Get capabilities failed: {ex.Message}");
                return new StreamingCapabilities { WebSocketEndpoint = null };
            }
        }
    }

    /// <summary>
    /// Streaming capability information for a specific engine.
    /// </summary>
    public class StreamingCapability
    {
        public string EngineId { get; set; } = string.Empty;
        public bool SupportsStreaming { get; set; }
        public bool SupportsBatch { get; set; }
        public string RecommendedMode { get; set; } = "batch";
        public string? FallbackMode { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Full streaming capabilities from the backend.
    /// </summary>
    public class StreamingCapabilities
    {
        public string? WebSocketEndpoint { get; set; }
        public List<string> StreamingEngines { get; set; } = new();
        public int TargetLatencyMs { get; set; } = 200;
        public int ChunkSizeSamples { get; set; } = 4800;
    }

    /// <summary>
    /// Represents an audio chunk received from streaming.
    /// </summary>
    public class AudioChunk
    {
        public int Index { get; set; }
        public byte[] AudioData { get; set; } = Array.Empty<byte>();
        public int SampleRate { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    /// <summary>
    /// Event args for audio chunk received.
    /// </summary>
    public class AudioChunkReceivedEventArgs : EventArgs
    {
        public int ChunkIndex { get; }
        public int ByteCount { get; }
        public int SampleRate { get; }

        public AudioChunkReceivedEventArgs(int chunkIndex, int byteCount, int sampleRate)
        {
            ChunkIndex = chunkIndex;
            ByteCount = byteCount;
            SampleRate = sampleRate;
        }
    }

    /// <summary>
    /// Event args for streaming errors.
    /// </summary>
    public class StreamingErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public Exception? Exception { get; }

        public StreamingErrorEventArgs(string message, Exception? exception)
        {
            Message = message;
            Exception = exception;
        }
    }

    /// <summary>
    /// Event args for synthesis completion.
    /// </summary>
    public class SynthesisCompleteEventArgs : EventArgs
    {
        public int TotalChunks { get; }
        public double DurationSeconds { get; }
        public string Engine { get; }

        public SynthesisCompleteEventArgs(int totalChunks, double duration, string engine)
        {
            TotalChunks = totalChunks;
            DurationSeconds = duration;
            Engine = engine;
        }
    }
}
