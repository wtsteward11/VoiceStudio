using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Tests.Services
{
    /// <summary>
    /// Tests for SearchOverlayCoordinator: panel routing, empty/bad panel ID, selection outcomes, MainWindow delegation.
    /// Per SEARCH_OVERLAY_SCOPING.md and MAINWINDOW_DECOMPOSITION_PLAN.
    /// </summary>
    [TestClass]
    [TestCategory("Services")]
    public class SearchOverlayCoordinatorTests
    {
        private Mock<IShellNavigationCoordinator> _mockShellNav = null!;
        private RecordingToastForSearchTests _toast = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockShellNav = new Mock<IShellNavigationCoordinator>();
            _toast = new RecordingToastForSearchTests();
        }

        private SearchOverlayCoordinator CreateCoordinator(Func<string, object?>? findName = null)
        {
            findName ??= _ => null;
            return new SearchOverlayCoordinator(findName, _mockShellNav.Object, _toast);
        }

        private static SearchResultItem CreateResult(
            string panelId = "library",
            string itemId = "item-1",
            string type = "profile",
            string title = "Test")
        {
            return new SearchResultItem
            {
                Id = itemId,
                PanelId = panelId,
                Type = type,
                Title = title
            };
        }

        [TestMethod]
        public void Show_WhenFindNameReturnsNull_DoesNotThrow()
        {
            var coordinator = CreateCoordinator(_ => null);
            coordinator.Show();
        }

        [TestMethod]
        public void Hide_WhenFindNameReturnsNull_DoesNotThrow()
        {
            var coordinator = CreateCoordinator(_ => null);
            coordinator.Hide();
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenPanelIdEmpty_ShowsErrorToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias(It.IsAny<string>())).Returns(string.Empty);

            var coordinator = CreateCoordinator();
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: ""));

            Assert.IsTrue(_toast.LastErrorToast.HasValue, "Empty panel ID should show error toast");
            var err = _toast.LastErrorToast!.Value;
            Assert.IsTrue(err.message.Contains("Unknown", StringComparison.Ordinal) ||
                         (err.title?.Contains("Panel Not Found", StringComparison.Ordinal) ?? false),
                "Error toast should indicate panel not found");
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenResolvePanelIdReturnsEmpty_ShowsErrorToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("unknown")).Returns(string.Empty);

            var coordinator = CreateCoordinator();
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "unknown"));

            Assert.IsTrue(_toast.LastErrorToast.HasValue, "Unknown panel should show error toast");
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenOpenPanelFails_ShowsErrorToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("library")).Returns("Library");
            _mockShellNav.Setup(x => x.GetPanelRegion("Library")).Returns(PanelRegion.Left);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left)).ReturnsAsync(false);

            var coordinator = CreateCoordinator();
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "library"));

            Assert.IsTrue(_toast.LastErrorToast.HasValue, "OpenPanel failure should show error toast");
            Assert.IsTrue(_toast.LastErrorToast!.Value.message.Contains("Library", StringComparison.Ordinal),
                "Error should mention panel ID");
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenOpenSucceeds_ButPanelHostMissing_ShowsWarningToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("library")).Returns("Library");
            _mockShellNav.Setup(x => x.GetPanelRegion("Library")).Returns(PanelRegion.Left);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left)).ReturnsAsync(true);

            var coordinator = CreateCoordinator(_ => null);
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "library", title: "My Item"));

            Assert.IsFalse(_toast.LastSuccessToast.HasValue, "Should not claim full success without host");
            Assert.IsTrue(_toast.LastWarningToast.HasValue, "Missing host should surface as warning");
            Assert.IsTrue(_toast.LastWarningToast!.Value.message.Contains("Library", StringComparison.Ordinal),
                "Warning should mention panel");
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenContentNotReady_ShowsWarningToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("library")).Returns("Library");
            _mockShellNav.Setup(x => x.GetPanelRegion("Library")).Returns(PanelRegion.Left);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left)).ReturnsAsync(true);

            // Production path: LeftPanelHost resolves to a non-PanelHost object → treated as missing host.
            var coordinator2 = CreateCoordinator(name => name == "LeftPanelHost" ? new object() : null);
            await coordinator2.HandleNavigateRequestedAsync(CreateResult(panelId: "library", title: "X"));

            Assert.IsTrue(_toast.LastWarningToast.HasValue);
            Assert.IsTrue(_toast.LastWarningToast!.Value.message.Contains("shell could not locate", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenPanelNotNavigable_ShowsInfoToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("library")).Returns("Library");
            _mockShellNav.Setup(x => x.GetPanelRegion("Library")).Returns(PanelRegion.Left);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left)).ReturnsAsync(true);

            var coordinator = CreateCoordinator();
            coordinator.PanelNavigationTestHook = _ => (null, null);
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "library", itemId: "a1", type: "audio"));

            Assert.IsTrue(_toast.LastInfoToast.HasValue);
            Assert.IsTrue(_toast.LastInfoToast!.Value.message.Contains("does not support", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenNavigableAndSelectionSucceeds_ShowsSuccessToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("library")).Returns("Library");
            _mockShellNav.Setup(x => x.GetPanelRegion("Library")).Returns(PanelRegion.Left);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left)).ReturnsAsync(true);

            var stub = new PlainNavigablePanelStub { NavigateHandler = (_, _, _, _) => Task.FromResult(true) };
            var coordinator = CreateCoordinator();
            coordinator.PanelNavigationTestHook = _ => (null, stub);
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "library", itemId: "id-99", type: "audio", title: "Clip"));

            Assert.IsTrue(_toast.LastSuccessToast.HasValue);
            Assert.IsTrue(_toast.LastSuccessToast!.Value.message.Contains("selected", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_toast.LastSuccessToast!.Value.message.Contains("Clip", StringComparison.Ordinal));
            Assert.AreEqual("id-99", stub.LastItemId);
            Assert.AreEqual("audio", stub.LastResultType);
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenNavigableButSelectionFails_ShowsWarningToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("library")).Returns("Library");
            _mockShellNav.Setup(x => x.GetPanelRegion("Library")).Returns(PanelRegion.Left);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left)).ReturnsAsync(true);

            var stub = new PlainNavigablePanelStub { NavigateHandler = (_, _, _, _) => Task.FromResult(false) };
            var coordinator = CreateCoordinator();
            coordinator.PanelNavigationTestHook = _ => (null, stub);
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "library", itemId: "missing", type: "audio", title: "Nope"));

            Assert.IsFalse(_toast.LastSuccessToast.HasValue);
            Assert.IsTrue(_toast.LastWarningToast.HasValue);
            Assert.IsTrue(_toast.LastWarningToast!.Value.title?.Contains("Selection", StringComparison.OrdinalIgnoreCase) ?? false);
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenItemIdEmpty_ShowsInfoWithoutSelection()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("library")).Returns("Library");
            _mockShellNav.Setup(x => x.GetPanelRegion("Library")).Returns(PanelRegion.Left);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left)).ReturnsAsync(true);

            var stub = new PlainNavigablePanelStub();
            var coordinator = CreateCoordinator();
            coordinator.PanelNavigationTestHook = _ => (null, stub);
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "library", itemId: "", type: "audio", title: "EmptyId"));

            Assert.IsTrue(_toast.LastInfoToast.HasValue);
            Assert.IsTrue(_toast.LastInfoToast!.Value.message.Contains("no item id", StringComparison.OrdinalIgnoreCase));
            Assert.IsNull(stub.LastItemId);
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_PassesMetadataToNavigable()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("timeline")).Returns("Timeline");
            _mockShellNav.Setup(x => x.GetPanelRegion("Timeline")).Returns(PanelRegion.Center);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Timeline", PanelRegion.Center)).ReturnsAsync(true);

            IReadOnlyDictionary<string, object>? captured = null;
            var stub = new PlainNavigablePanelStub
            {
                NavigateHandler = (_, _, _, meta) =>
                {
                    captured = meta;
                    return Task.FromResult(true);
                }
            };
            var coordinator = CreateCoordinator();
            coordinator.PanelNavigationTestHook = _ => (null, stub);
            var r = CreateResult(panelId: "timeline", itemId: "m1", type: "marker", title: "M");
            r.Metadata["project_id"] = "proj-1";
            await coordinator.HandleNavigateRequestedAsync(r);

            Assert.IsNotNull(captured);
            Assert.IsTrue(captured!.TryGetValue("project_id", out var pid));
            Assert.AreEqual("proj-1", pid?.ToString());
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenOpenPanelSucceeds_CallsOpenPanelByIdAsyncWithResolvedId()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("profiles")).Returns("Profiles");
            _mockShellNav.Setup(x => x.GetPanelRegion("Profiles")).Returns(PanelRegion.Right);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Profiles", PanelRegion.Right)).ReturnsAsync(true);

            var coordinator = CreateCoordinator();
            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "profiles"));

            _mockShellNav.Verify(x => x.ResolvePanelIdAlias("profiles"), Times.Once);
            _mockShellNav.Verify(x => x.OpenPanelByIdAsync("Profiles", PanelRegion.Right), Times.Once);
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_WhenThrows_ShowsErrorToast()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias(It.IsAny<string>())).Throws(new System.Exception("Backend down"));

            var coordinator = CreateCoordinator();
            await coordinator.HandleNavigateRequestedAsync(CreateResult());

            Assert.IsTrue(_toast.LastErrorToast.HasValue, "Exception should surface as error toast");
            Assert.IsTrue(_toast.LastErrorToast!.Value.message.Contains("Backend down", StringComparison.Ordinal),
                "Error message should contain exception text");
        }

        [TestMethod]
        public async Task HandleNavigateRequestedAsync_RepeatedCalls_UseCorrectPanelIdPerCall()
        {
            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("library")).Returns("Library");
            _mockShellNav.Setup(x => x.GetPanelRegion("Library")).Returns(PanelRegion.Left);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left)).ReturnsAsync(true);

            _mockShellNav.Setup(x => x.ResolvePanelIdAlias("profiles")).Returns("Profiles");
            _mockShellNav.Setup(x => x.GetPanelRegion("Profiles")).Returns(PanelRegion.Right);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Profiles", PanelRegion.Right)).ReturnsAsync(true);

            var coordinator = CreateCoordinator();

            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "library", itemId: "item-1"));
            _mockShellNav.Verify(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left), Times.Once);
            _mockShellNav.Verify(x => x.OpenPanelByIdAsync("Profiles", PanelRegion.Right), Times.Never);

            await coordinator.HandleNavigateRequestedAsync(CreateResult(panelId: "profiles", itemId: "item-2"));
            _mockShellNav.Verify(x => x.OpenPanelByIdAsync("Library", PanelRegion.Left), Times.Once);
            _mockShellNav.Verify(x => x.OpenPanelByIdAsync("Profiles", PanelRegion.Right), Times.Once);
        }

        [TestMethod]
        public void MainWindow_DoesNotContainSearchOrchestrationLogic_DelegateOnly()
        {
            var assemblyDir = Path.GetDirectoryName(typeof(SearchOverlayCoordinatorTests).Assembly.Location) ?? ".";
            var mainWindowPath = Path.GetFullPath(Path.Combine(
                assemblyDir, "..", "..", "..", "..", "..", "VoiceStudio.App", "MainWindow.xaml.cs"));
            if (!File.Exists(mainWindowPath))
                Assert.Inconclusive($"MainWindow.xaml.cs not found at {mainWindowPath}");

            var source = File.ReadAllText(mainWindowPath);
            Assert.IsFalse(source.Contains("NavigateToSearchResultAsync") && source.Contains("private async Task"),
                "MainWindow should not contain NavigateToSearchResultAsync (moved to coordinator)");
            Assert.IsFalse(source.Contains("TrySelectItemInPanelAsync"),
                "MainWindow should not contain TrySelectItemInPanelAsync (moved to coordinator)");
        }

        private sealed class PlainNavigablePanelStub : INavigatablePanel
        {
            public Func<string, string, CancellationToken, IReadOnlyDictionary<string, object>?, Task<bool>>? NavigateHandler { get; set; }

            public string? LastItemId { get; private set; }

            public string? LastResultType { get; private set; }

            public Task<bool> NavigateToItemAsync(
                string itemId,
                string resultType,
                CancellationToken ct,
                IReadOnlyDictionary<string, object>? searchMetadata = null)
            {
                LastItemId = itemId;
                LastResultType = resultType;
                if (NavigateHandler != null)
                    return NavigateHandler(itemId, resultType, ct, searchMetadata);
                return Task.FromResult(true);
            }
        }

        internal sealed class RecordingToastForSearchTests : IToastNotificationService
        {
            public (ToastType type, string message, string? title)? LastErrorToast { get; private set; }
            public (ToastType type, string message, string? title)? LastSuccessToast { get; private set; }
            public (ToastType type, string message, string? title)? LastWarningToast { get; private set; }
            public (ToastType type, string message, string? title)? LastInfoToast { get; private set; }

            public void ShowToast(ToastType type, string message, string? title = null)
            {
                var tuple = (type, message, title);
                switch (type)
                {
                    case ToastType.Error:
                        LastErrorToast = tuple;
                        break;
                    case ToastType.Success:
                        LastSuccessToast = tuple;
                        break;
                    case ToastType.Warning:
                        LastWarningToast = tuple;
                        break;
                    case ToastType.Info:
                        LastInfoToast = tuple;
                        break;
                }
            }

            public void ShowInfo(string message, string? title = null)
            {
                ShowToast(ToastType.Info, message, title);
            }

            public void ShowSuccess(string message, string? title = null)
            {
                ShowToast(ToastType.Success, message, title);
            }

            public void ShowWarning(string message, string? title = null)
            {
                ShowToast(ToastType.Warning, message, title);
            }

            public void ShowError(string message, string? title = null, Action? viewDetailsAction = null, string? actionButtonLabel = null)
            {
                ShowToast(ToastType.Error, message, title);
            }
        }
    }
}
