using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01 — JSON persistence schema and round-trip proofs.
/// </summary>
[TestClass]
[TestCategory("Services")]
public sealed class JsonProjectRepositoryTests
{
    private static string NewTempRepoDir() =>
        Path.Combine(Path.GetTempPath(), "vs_json_repo_test_" + Guid.NewGuid().ToString("N"));

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[JsonProjectRepositoryTests] Temp dir cleanup (IO): {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[JsonProjectRepositoryTests] Temp dir cleanup (access): {ex.Message}");
        }
    }

    [TestMethod]
    public async Task SaveAsync_roundTrip_sets_PersistedProjectSchemaVersion_and_tracks()
    {
        var dir = NewTempRepoDir();
        try
        {
            var repo = new JsonProjectRepository(dir);
            var p = new Project
            {
                Id = "proj-rt-1",
                Name = "RoundTrip",
                CreatedAt = DateTime.UtcNow.ToString("o"),
                UpdatedAt = DateTime.UtcNow.ToString("o"),
                Tracks =
                {
                    new AudioTrack { Id = "t1", Name = "Track 1", ProjectId = "proj-rt-1", TrackNumber = 1 }
                }
            };

            await repo.SaveAsync(p);

            var loaded = await repo.GetByIdAsync("proj-rt-1");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(JsonProjectRepository.CurrentPersistedProjectSchemaVersion, loaded!.PersistedProjectSchemaVersion);
            Assert.AreEqual(1, loaded.Tracks.Count);
            Assert.AreEqual("t1", loaded.Tracks[0].Id);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [TestMethod]
    public async Task GetByIdAsync_throws_InvalidDataException_when_schema_newer_than_app()
    {
        var dir = NewTempRepoDir();
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "future-proj.json");
            const string json = """{"id":"future-proj","name":"X","createdAt":"2020-01-01T00:00:00Z","updatedAt":"2020-01-01T00:00:00Z","persistedProjectSchemaVersion":999,"voiceProfileIds":[],"tracks":[]}""";
            await File.WriteAllTextAsync(path, json);

            var repo = new JsonProjectRepository(dir);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => repo.GetByIdAsync("future-proj"));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [TestMethod]
    public async Task GetByIdAsync_accepts_legacy_json_without_persistedProjectSchemaVersion()
    {
        var dir = NewTempRepoDir();
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "legacy-proj.json");
            const string json = """{"id":"legacy-proj","name":"Old","createdAt":"2020-01-01T00:00:00Z","updatedAt":"2020-01-01T00:00:00Z","voiceProfileIds":[],"tracks":[]}""";
            await File.WriteAllTextAsync(path, json);

            var repo = new JsonProjectRepository(dir);
            var loaded = await repo.GetByIdAsync("legacy-proj");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(0, loaded!.PersistedProjectSchemaVersion);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    /// <summary>GAP-045 lifecycle: last-subtitle id patch persists and clears via repository API.</summary>
    [TestMethod]
    public async Task SaveLastSubtitleTranscriptionIdAsync_roundTrip_then_clear()
    {
        var dir = NewTempRepoDir();
        try
        {
            var repo = new JsonProjectRepository(dir);
            var p = new Project
            {
                Id = "proj-sub",
                Name = "Sub",
                CreatedAt = DateTime.UtcNow.ToString("o"),
                UpdatedAt = DateTime.UtcNow.ToString("o"),
                Tracks = new List<AudioTrack>(),
            };
            await repo.SaveAsync(p);

            await repo.SaveLastSubtitleTranscriptionIdAsync("proj-sub", "tid-42", CancellationToken.None);
            var got = await repo.GetLastSubtitleTranscriptionIdAsync("proj-sub");
            Assert.AreEqual("tid-42", got);

            await repo.SaveLastSubtitleTranscriptionIdAsync("proj-sub", null, CancellationToken.None);
            var cleared = await repo.GetLastSubtitleTranscriptionIdAsync("proj-sub");
            Assert.IsNull(cleared);

            var loaded = await repo.GetByIdAsync("proj-sub");
            Assert.IsNotNull(loaded);
            Assert.IsNull(loaded!.LastSubtitleTranscriptionId);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }
}
