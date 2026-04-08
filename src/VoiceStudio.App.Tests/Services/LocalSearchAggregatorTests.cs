using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public class LocalSearchAggregatorTests
{
    [TestMethod]
    public async Task SearchAsync_WhenBackendFails_ReturnsLocalResults()
    {
        var searchClient = new Mock<ISearchClient>();
        searchClient
            .Setup(x => x.SearchAsync("theme", null, 50, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Exception("backend unavailable"));

        var localProvider = new Mock<ILocalSearchProvider>();
        localProvider
            .Setup(x => x.SearchAsync("theme", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResultItem>
            {
                new()
                {
                    Id = "settings.theme",
                    Type = "setting",
                    Title = "Theme",
                    PanelId = "settings"
                }
            });

        var sut = new LocalSearchAggregator(searchClient.Object, new[] { localProvider.Object });

        var result = await sut.SearchAsync("theme");

        Assert.AreEqual(1, result.TotalResults);
        Assert.AreEqual("settings.theme", result.Results[0].Id);
        Assert.IsTrue(result.ResultsByType.ContainsKey("setting"));
    }

    [TestMethod]
    public async Task SearchAsync_MergesBackendAndLocalWithoutDuplicates()
    {
        var searchClient = new Mock<ISearchClient>();
        searchClient
            .Setup(x => x.SearchAsync("play", null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponse
            {
                Query = "play",
                Results = new List<SearchResultItem>
                {
                    new() { Id = "command:playback.play", Type = "command", Title = "Play", PanelId = "command-palette" }
                }
            });

        var localProvider = new Mock<ILocalSearchProvider>();
        localProvider
            .Setup(x => x.SearchAsync("play", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResultItem>
            {
                new() { Id = "command:playback.play", Type = "command", Title = "Play", PanelId = "command-palette" },
                new() { Id = "settings.audio", Type = "setting", Title = "Audio Settings", PanelId = "settings" }
            });

        var sut = new LocalSearchAggregator(searchClient.Object, new[] { localProvider.Object });

        var result = await sut.SearchAsync("play");

        Assert.AreEqual(2, result.TotalResults);
        Assert.IsTrue(result.ResultsByType.ContainsKey("command"));
        Assert.IsTrue(result.ResultsByType.ContainsKey("setting"));
    }
}
