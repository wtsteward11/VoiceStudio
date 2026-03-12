// Copyright (c) VoiceStudio. All rights reserved.
// Licensed under the MIT license.

using System;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Factory for creating WebSocket clients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This factory centralizes WebSocket client creation, enabling:
    /// - Dependency injection in ViewModels
    /// - Testability via mock factories
    /// - Centralized WebSocket service access
    /// </para>
    /// <para>
    /// GAP-009: Replaces direct instantiation with factory pattern.
    /// Phase 6: Pipeline uses direct connect to /api/pipeline/stream when backend URL is available.
    /// Phase 7: RVC uses direct connect to /api/rvc/convert/realtime when backend URL is available.
    /// </para>
    /// </remarks>
    public class WebSocketClientFactory : IWebSocketClientFactory
    {
        private readonly IWebSocketService? _webSocketService;
        private readonly string? _backendBaseUrl;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocketClientFactory"/> class.
        /// </summary>
        /// <param name="webSocketService">The WebSocket service to use for client creation.</param>
        /// <param name="backendBaseUrl">Optional backend base URL for direct pipeline/RVC connections.</param>
        public WebSocketClientFactory(IWebSocketService? webSocketService, string? backendBaseUrl = null)
        {
            _webSocketService = webSocketService;
            _backendBaseUrl = backendBaseUrl;
        }

        /// <inheritdoc/>
        public IRealtimeVoiceClient? CreateRealtimeVoiceClient()
        {
            if (!string.IsNullOrEmpty(_backendBaseUrl))
                return new RealtimeVoiceWebSocketClient(_backendBaseUrl);

            if (_webSocketService != null)
                return new RealtimeVoiceWebSocketClient(_webSocketService);

            return null;
        }

        /// <inheritdoc/>
        public IPipelineStreamingClient? CreatePipelineStreamingClient()
        {
            if (!string.IsNullOrEmpty(_backendBaseUrl))
                return new PipelineStreamingWebSocketClient(_backendBaseUrl);

            if (_webSocketService != null)
                return new PipelineStreamingWebSocketClient(_webSocketService);

            return null;
        }

        /// <inheritdoc/>
        public IJobProgressClient? CreateJobProgressClient()
        {
            if (_webSocketService == null)
                return null;

            return new JobProgressWebSocketClient(_webSocketService);
        }
    }
}
