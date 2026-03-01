using System;

namespace VoiceStudio.App.Config
{
    /// <summary>
    /// Centralized application configuration with environment variable overrides.
    /// 
    /// Environment Variables:
    ///     VOICESTUDIO_API_HOST: API host (default: localhost)
    ///     VOICESTUDIO_API_PORT: API port (default: 8000)
    ///     VOICESTUDIO_WS_PORT: WebSocket port (default: 8000)
    ///     VOICESTUDIO_HEALTH_CHECK_INTERVAL: Health check interval in ms (default: 5000)
    ///     VOICESTUDIO_RECONNECT_DELAY: Reconnect delay in ms (default: 3000)
    ///     VOICESTUDIO_REQUEST_TIMEOUT: Request timeout in ms (default: 30000)
    ///     VOICESTUDIO_DEBUG: Enable debug mode (default: false)
    /// </summary>
    public static class AppConfig
    {
        #region API Configuration

        /// <summary>
        /// Get the API host address.
        /// </summary>
        public static string ApiHost =>
            Environment.GetEnvironmentVariable("VOICESTUDIO_API_HOST") ?? "localhost";

        /// <summary>
        /// Get the API port.
        /// </summary>
        public static int ApiPort =>
            GetEnvInt("VOICESTUDIO_API_PORT", 8000);

        /// <summary>
        /// Get the WebSocket port.
        /// </summary>
        public static int WebSocketPort =>
            GetEnvInt("VOICESTUDIO_WS_PORT", 8000);

        /// <summary>
        /// Get the full API base URL.
        /// </summary>
        public static string ApiUrl => $"http://{ApiHost}:{ApiPort}";

        /// <summary>
        /// Get the WebSocket URL.
        /// </summary>
        public static string WebSocketUrl => $"ws://{ApiHost}:{WebSocketPort}";

        #endregion

        #region Timing Configuration

        /// <summary>
        /// Get health check interval in milliseconds.
        /// </summary>
        public static int HealthCheckIntervalMs =>
            GetEnvInt("VOICESTUDIO_HEALTH_CHECK_INTERVAL", 5000);

        /// <summary>
        /// Get reconnect delay in milliseconds.
        /// </summary>
        public static int ReconnectDelayMs =>
            GetEnvInt("VOICESTUDIO_RECONNECT_DELAY", 3000);

        /// <summary>
        /// Get request timeout in milliseconds.
        /// </summary>
        public static int RequestTimeoutMs =>
            GetEnvInt("VOICESTUDIO_REQUEST_TIMEOUT", 30000);

        /// <summary>
        /// Get connect timeout in milliseconds.
        /// </summary>
        public static int ConnectTimeoutMs =>
            GetEnvInt("VOICESTUDIO_CONNECT_TIMEOUT", 10000);

        /// <summary>
        /// Get synthesis timeout in milliseconds.
        /// </summary>
        public static int SynthesisTimeoutMs =>
            GetEnvInt("VOICESTUDIO_SYNTHESIS_TIMEOUT", 120000);

        /// <summary>
        /// Get transcription timeout in milliseconds.
        /// </summary>
        public static int TranscriptionTimeoutMs =>
            GetEnvInt("VOICESTUDIO_TRANSCRIPTION_TIMEOUT", 300000);

        #endregion

        #region Retry Configuration

        /// <summary>
        /// Get maximum retry count for operations.
        /// </summary>
        public static int MaxRetries =>
            GetEnvInt("VOICESTUDIO_MAX_RETRIES", 3);

        /// <summary>
        /// Get delay between retries in milliseconds.
        /// </summary>
        public static int RetryDelayMs =>
            GetEnvInt("VOICESTUDIO_RETRY_DELAY", 1000);

        #endregion

        #region Buffer Configuration

        /// <summary>
        /// Get default buffer size in bytes.
        /// </summary>
        public static int BufferSize =>
            GetEnvInt("VOICESTUDIO_BUFFER_SIZE", 4096);

        /// <summary>
        /// Get audio chunk size in samples.
        /// </summary>
        public static int ChunkSize =>
            GetEnvInt("VOICESTUDIO_CHUNK_SIZE", 4000);

        /// <summary>
        /// Get maximum file size in megabytes.
        /// </summary>
        public static int MaxFileSizeMb =>
            GetEnvInt("VOICESTUDIO_MAX_FILE_SIZE_MB", 100);

        /// <summary>
        /// Get maximum backup size in megabytes.
        /// </summary>
        public static int MaxBackupSizeMb =>
            GetEnvInt("VOICESTUDIO_MAX_BACKUP_SIZE_MB", 500);

        #endregion

        #region Feature Flags

        /// <summary>
        /// Check if debug mode is enabled.
        /// </summary>
        public static bool IsDebugMode =>
            GetEnvBool("VOICESTUDIO_DEBUG", false);

        /// <summary>
        /// Check if telemetry is enabled (opt-in only).
        /// </summary>
        public static bool IsTelemetryEnabled =>
            GetEnvBool("VOICESTUDIO_TELEMETRY_ENABLED", false);

        #endregion

        #region Helper Methods

        private static int GetEnvInt(string key, int defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private static bool GetEnvBool(string key, bool defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.Ordinal) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
