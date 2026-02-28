using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Fixtures
{
    /// <summary>
    /// Provides factory methods for creating test data objects.
    /// Use these methods to create consistent test data across test classes.
    /// </summary>
    public static class TestDataGenerators
    {
        private static int _idCounter = 1;

        /// <summary>
        /// Resets the ID counter. Call this in test cleanup if needed.
        /// </summary>
        public static void ResetIdCounter()
        {
            _idCounter = 1;
        }

        /// <summary>
        /// Generates a unique ID for test objects.
        /// </summary>
        /// <param name="prefix">The prefix to use for the ID.</param>
        /// <returns>A unique ID string.</returns>
        public static string GenerateId(string prefix = "test")
        {
            return $"{prefix}_{Interlocked.Increment(ref _idCounter):D6}";
        }

        #region Voice Profiles

        /// <summary>
        /// Creates a test voice profile with default values.
        /// </summary>
        /// <param name="id">Optional ID override.</param>
        /// <param name="name">Optional name override.</param>
        /// <param name="language">Optional language override.</param>
        /// <returns>A new VoiceProfile instance.</returns>
        public static VoiceProfile CreateVoiceProfile(
            string? id = null,
            string? name = null,
            string? language = null)
        {
            return new VoiceProfile
            {
                Id = id ?? GenerateId("profile"),
                Name = name ?? $"Test Profile {_idCounter}",
                Language = language ?? "en-US",
                Emotion = "neutral",
                QualityScore = 0.85,
                Tags = new List<string> { "test", "automated" }
            };
        }

        /// <summary>
        /// Creates multiple test voice profiles.
        /// </summary>
        /// <param name="count">Number of profiles to create.</param>
        /// <returns>A list of VoiceProfile instances.</returns>
        public static List<VoiceProfile> CreateVoiceProfiles(int count)
        {
            return Enumerable.Range(1, count)
                .Select(_ => CreateVoiceProfile())
                .ToList();
        }

        #endregion Voice Profiles

        #region Projects

        /// <summary>
        /// Creates a test project with default values.
        /// </summary>
        /// <param name="id">Optional ID override.</param>
        /// <param name="name">Optional name override.</param>
        /// <returns>A new Project instance.</returns>
        public static Project CreateProject(
            string? id = null,
            string? name = null)
        {
            var projectId = id ?? GenerateId("project");
            var now = DateTime.UtcNow.ToString("o");
            return new Project
            {
                Id = projectId,
                Name = name ?? $"Test Project {_idCounter}",
                Description = "A test project",
                CreatedAt = now,
                UpdatedAt = now,
                VoiceProfileIds = new List<string>(),
                Tracks = new List<AudioTrack>()
            };
        }

        /// <summary>
        /// Creates a test project with tracks.
        /// </summary>
        /// <param name="trackCount">Number of tracks to create.</param>
        /// <returns>A Project with the specified number of tracks.</returns>
        public static Project CreateProjectWithTracks(int trackCount)
        {
            var project = CreateProject();
            project.Tracks = CreateTracks(trackCount);
            return project;
        }

        #endregion Projects

        #region Tracks and Clips

        /// <summary>
        /// Creates a test audio track with default values.
        /// </summary>
        /// <param name="id">Optional ID override.</param>
        /// <param name="name">Optional name override.</param>
        /// <param name="trackNumber">Optional track number.</param>
        /// <returns>A new AudioTrack instance.</returns>
        public static AudioTrack CreateTrack(
            string? id = null,
            string? name = null,
            int trackNumber = 1)
        {
            return new AudioTrack
            {
                Id = id ?? GenerateId("track"),
                Name = name ?? $"Track {_idCounter}",
                TrackNumber = trackNumber,
                IsMuted = false,
                IsSolo = false,
                Clips = new List<AudioClip>()
            };
        }

        /// <summary>
        /// Creates multiple test tracks.
        /// </summary>
        /// <param name="count">Number of tracks to create.</param>
        /// <returns>A list of AudioTrack instances.</returns>
        public static List<AudioTrack> CreateTracks(int count)
        {
            return Enumerable.Range(1, count)
                .Select(_ => CreateTrack())
                .ToList();
        }

        /// <summary>
        /// Creates a test audio clip with default values.
        /// </summary>
        /// <param name="id">Optional ID override.</param>
        /// <param name="startSeconds">Start time in seconds.</param>
        /// <param name="durationSeconds">Duration in seconds.</param>
        /// <returns>A new AudioClip instance.</returns>
        public static AudioClip CreateClip(
            string? id = null,
            double startSeconds = 0.0,
            double durationSeconds = 5.0)
        {
            return new AudioClip
            {
                Id = id ?? GenerateId("clip"),
                Name = $"Clip {_idCounter}",
                StartTime = startSeconds,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                AudioId = GenerateId("audio"),
                AudioUrl = $"http://localhost:8001/audio/{GenerateId("audio")}.wav"
            };
        }

        /// <summary>
        /// Creates multiple test clips.
        /// </summary>
        /// <param name="count">Number of clips to create.</param>
        /// <returns>A list of AudioClip instances.</returns>
        public static List<AudioClip> CreateClips(int count)
        {
            return Enumerable.Range(1, count)
                .Select(i => CreateClip(startSeconds: i * 5.0))
                .ToList();
        }

        #endregion Tracks and Clips

        #region Settings

        /// <summary>
        /// Creates default test settings.
        /// </summary>
        /// <returns>A new SettingsData instance with default values.</returns>
        public static SettingsData CreateDefaultSettings()
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
                    SnapEnabled = true,
                    GridEnabled = true
                },
                Backend = new BackendSettings
                {
                    ApiUrl = "http://localhost:8001"
                },
                Performance = new PerformanceSettings
                {
                    CachingEnabled = true,
                    CacheSize = 512
                }
            };
        }

        #endregion Settings

        #region Search Results

        /// <summary>
        /// Creates a test search response.
        /// </summary>
        /// <param name="resultCount">Number of results to include.</param>
        /// <param name="query">Optional search query.</param>
        /// <returns>A SearchResponse with the specified number of results.</returns>
        public static SearchResponse CreateSearchResponse(int resultCount = 5, string query = "test")
        {
            var results = Enumerable.Range(1, resultCount)
                .Select(i => new SearchResultItem
                {
                    Id = GenerateId("result"),
                    Title = $"Search Result {i}",
                    Type = "audio",
                    PanelId = "voice-synthesis",
                    Description = $"This is the description for result {i}"
                })
                .ToList();

            return new SearchResponse
            {
                Query = query,
                Results = results,
                TotalResults = resultCount,
                ResultsByType = new Dictionary<string, int> { { "audio", resultCount } }
            };
        }

        #endregion Search Results

        #region Test Helpers

        /// <summary>
        /// Creates a random string of specified length.
        /// </summary>
        /// <param name="length">The length of the string.</param>
        /// <returns>A random string.</returns>
        public static string RandomString(int length = 10)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        /// <summary>
        /// Creates a test file path.
        /// </summary>
        /// <param name="extension">The file extension.</param>
        /// <returns>A test file path string.</returns>
        public static string CreateTestFilePath(string extension = "wav")
        {
            return $"C:\\TestData\\{GenerateId("file")}.{extension}";
        }

        #endregion Test Helpers
    }
}
