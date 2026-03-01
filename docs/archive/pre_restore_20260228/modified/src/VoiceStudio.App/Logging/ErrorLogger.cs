// Copyright (c) VoiceStudio. All rights reserved.
// Licensed under the MIT License.

using System;
using VoiceStudio.App.Logging;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VoiceStudio.App.Logging
{
    /// <summary>
    /// Centralized error logging with structured output.
    /// Used by ErrorBoundary and other error handling infrastructure.
    /// </summary>
    public static class ErrorLogger
    {
        private static readonly object _lock = new();
        private static readonly string _logDirectory;
        private static readonly string _logFilePath;

        static ErrorLogger()
        {
            // Use local app data for logs
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(appData, "VoiceStudio", "logs");
            Directory.CreateDirectory(_logDirectory);

            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            _logFilePath = Path.Combine(_logDirectory, $"voicestudio-{date}.log");
        }

        /// <summary>
        /// Logs a warning message with optional structured context.
        /// </summary>
        /// <param name="message">The warning message.</param>
        /// <param name="source">The source of the warning (component name).</param>
        /// <param name="context">Optional structured context data.</param>
        public static void LogWarning(string message, string source = "", IDictionary<string, object>? context = null)
        {
            Log("WARNING", message, source, context);
        }

        /// <summary>
        /// Logs an error message with optional structured context.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="source">The source of the error (component name).</param>
        /// <param name="context">Optional structured context data.</param>
        public static void LogError(string message, string source = "", IDictionary<string, object>? context = null)
        {
            Log("ERROR", message, source, context);
        }

        /// <summary>
        /// Logs an info message with optional structured context.
        /// </summary>
        /// <param name="message">The info message.</param>
        /// <param name="source">The source of the message (component name).</param>
        /// <param name="context">Optional structured context data.</param>
        public static void LogInfo(string message, string source = "", IDictionary<string, object>? context = null)
        {
            Log("INFO", message, source, context);
        }

        /// <summary>
        /// Logs a debug message with optional structured context.
        /// </summary>
        /// <param name="message">The debug message.</param>
        /// <param name="source">The source of the message (component name).</param>
        /// <param name="context">Optional structured context data.</param>
        public static void LogDebug(string message, string source = "", IDictionary<string, object>? context = null)
        {
#if DEBUG
            Log("DEBUG", message, source, context);
#endif
        }

        private static void Log(string level, string message, string source, IDictionary<string, object>? context)
        {
            var timestamp = DateTime.UtcNow.ToString("o");
            var entry = new Dictionary<string, object>
            {
                ["timestamp"] = timestamp,
                ["level"] = level,
                ["message"] = message,
                ["source"] = source
            };

            if (context != null)
            {
                foreach (var kvp in context)
                {
                    entry[$"ctx_{kvp.Key}"] = kvp.Value;
                }
            }

            var json = JsonSerializer.Serialize(entry);

            // Write to debug output (use Debug.WriteLine to avoid recursion)
            System.Diagnostics.Debug.WriteLine($"[{level}] [{source}] {message}");

            // Write to file
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, json + Environment.NewLine);
                }
            }
            catch
            {
                // Best effort logging - don't fail if log write fails
                System.Diagnostics.Debug.WriteLine($"[ErrorLogger] Failed to write log entry to file: {_logFilePath}");
            }
        }

        /// <summary>
        /// Gets the path to the current log file.
        /// </summary>
        public static string LogFilePath => _logFilePath;

        /// <summary>
        /// Gets the path to the log directory.
        /// </summary>
        public static string LogDirectory => _logDirectory;
    }
}
