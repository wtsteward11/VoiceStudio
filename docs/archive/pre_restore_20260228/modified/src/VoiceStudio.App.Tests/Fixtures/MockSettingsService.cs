using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Fixtures
{
    /// <summary>
    /// Mock implementation of ISettingsService for testing.
    /// Stores settings in memory and tracks method calls.
    /// </summary>
    public class MockSettingsService : ISettingsService
    {
        private readonly ConcurrentDictionary<string, object> _categorySettings = new();
        private SettingsData _settings;
        private readonly object _lock = new();

        public MockSettingsService()
        {
            _settings = GetDefaultSettings();
        }

        /// <summary>
        /// Number of times LoadSettingsAsync was called.
        /// </summary>
        public int LoadSettingsCallCount { get; private set; }

        /// <summary>
        /// Number of times SaveSettingsAsync was called.
        /// </summary>
        public int SaveSettingsCallCount { get; private set; }

        /// <summary>
        /// Exception to throw on next LoadSettings call (for error testing).
        /// </summary>
        public Exception? LoadSettingsException { get; set; }

        /// <summary>
        /// Exception to throw on next SaveSettings call (for error testing).
        /// </summary>
        public Exception? SaveSettingsException { get; set; }

        /// <summary>
        /// Delay to simulate async operations (in milliseconds).
        /// </summary>
        public int SimulatedDelayMs { get; set; }

        /// <summary>
        /// Gets or sets the settings data returned by LoadSettingsAsync.
        /// </summary>
        public SettingsData Settings
        {
            get
            {
                lock (_lock)
                {
                    return _settings;
                }
            }
            set
            {
                lock (_lock)
                {
                    _settings = value;
                }
            }
        }

        /// <summary>
        /// Clears all stored settings and resets call counts.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _settings = GetDefaultSettings();
                _categorySettings.Clear();
                LoadSettingsCallCount = 0;
                SaveSettingsCallCount = 0;
                LoadSettingsException = null;
                SaveSettingsException = null;
            }
        }

        /// <summary>
        /// Sets a category setting for testing.
        /// </summary>
        /// <typeparam name="T">The type of the settings category.</typeparam>
        /// <param name="category">The category name.</param>
        /// <param name="settings">The settings object.</param>
        public void SetCategorySettings<T>(string category, T settings) where T : class
        {
            _categorySettings[category] = settings;
        }

        public async Task<SettingsData> LoadSettingsAsync(CancellationToken cancellationToken = default)
        {
            LoadSettingsCallCount++;

            if (SimulatedDelayMs > 0)
            {
                await Task.Delay(SimulatedDelayMs, cancellationToken);
            }

            if (LoadSettingsException != null)
            {
                throw LoadSettingsException;
            }

            return Settings;
        }

        public async Task<T?> LoadCategoryAsync<T>(string category, CancellationToken cancellationToken = default) where T : class
        {
            if (SimulatedDelayMs > 0)
            {
                await Task.Delay(SimulatedDelayMs, cancellationToken);
            }

            if (_categorySettings.TryGetValue(category, out var settings))
            {
                return settings as T;
            }

            return default;
        }

        public async Task SaveSettingsAsync(SettingsData settings, CancellationToken cancellationToken = default)
        {
            SaveSettingsCallCount++;

            if (SimulatedDelayMs > 0)
            {
                await Task.Delay(SimulatedDelayMs, cancellationToken);
            }

            if (SaveSettingsException != null)
            {
                throw SaveSettingsException;
            }

            lock (_lock)
            {
                _settings = settings;
            }
        }

        public async Task<T> UpdateCategoryAsync<T>(string category, T categorySettings, CancellationToken cancellationToken = default) where T : class
        {
            if (SimulatedDelayMs > 0)
            {
                await Task.Delay(SimulatedDelayMs, cancellationToken);
            }

            _categorySettings[category] = categorySettings;
            return categorySettings;
        }

        public Task<SettingsData> ResetSettingsAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _settings = GetDefaultSettings();
                _categorySettings.Clear();
            }
            return Task.FromResult(_settings);
        }

        public bool ValidateSettings(SettingsData settings, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public SettingsData GetDefaultSettings()
        {
            return new SettingsData
            {
                General = new GeneralSettings
                {
                    Theme = "Dark",
                    Language = "en-US",
                    AutoSave = true,
                    AutoSaveInterval = 300
                },
                Engine = new EngineSettings
                {
                    DefaultAudioEngine = "xtts",
                    DefaultImageEngine = "sdxl",
                    DefaultVideoEngine = "svd",
                    QualityLevel = 5
                },
                Audio = new AudioSettings
                {
                    SampleRate = 44100,
                    BufferSize = 1024
                },
                Timeline = new TimelineSettings
                {
                    SnapEnabled = true
                },
                Backend = new BackendSettings
                {
                    ApiUrl = "http://localhost:8000"
                },
                Performance = new PerformanceSettings
                {
                    CachingEnabled = true
                },
                Plugins = new PluginSettings(),
                Mcp = new McpSettings(),
                Quality = new QualitySettings(),
                Diagnostics = new DiagnosticsSettings()
            };
        }
    }
}
