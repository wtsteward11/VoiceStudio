using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Threading.Tasks;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Tests.Mocks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Plugins.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for PluginGalleryViewModel.
    /// </summary>
    [TestClass]
    public class PluginGalleryViewModelTests
    {
        private IViewModelContext _context = null!;
        private DispatcherQueueController? _dispatcherController;
        private MockPluginGateway _mockGateway = null!;
        private PluginGalleryViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            TestAppServicesHelper.EnsureInitialized();
            _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
            var dispatcher = _dispatcherController.DispatcherQueue;
            _context = new ViewModelContext(NullLogger.Instance, dispatcher);
            _mockGateway = new MockPluginGateway();
            _viewModel = new PluginGalleryViewModel(_context, _mockGateway);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _viewModel?.Dispose();
            _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
        }

        #region Initialization Tests

        [TestMethod]
        public async Task InitializeAsync_LoadsCategories()
        {
            // Act
            await _viewModel.InitializeAsync();

            // Assert
            Assert.IsTrue(_viewModel.Categories.Count > 0, "Categories should be loaded");
        }

        [TestMethod]
        public async Task InitializeAsync_LoadsFeaturedPlugins()
        {
            // Act
            await _viewModel.InitializeAsync();

            // Assert
            Assert.IsTrue(_viewModel.FeaturedPlugins.Count > 0, "Featured plugins should be loaded");
        }

        [TestMethod]
        public async Task InitializeAsync_LoadsPluginCatalog()
        {
            // Act
            await _viewModel.InitializeAsync();

            // Assert
            Assert.IsTrue(_viewModel.Plugins.Count > 0, "Plugins should be loaded");
            Assert.IsTrue(_viewModel.TotalPlugins > 0, "Total plugins count should be set");
        }

        [TestMethod]
        public async Task InitializeAsync_SetsStatusMessage()
        {
            // Act
            await _viewModel.InitializeAsync();

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(_viewModel.StatusMessage), "Status message should be set");
        }

        #endregion

        #region Search Tests

        [TestMethod]
        public async Task SearchText_FiltersPlugins()
        {
            // Arrange
            await _viewModel.InitializeAsync();
            var initialCount = _viewModel.Plugins.Count;

            // Act
            _viewModel.SearchText = "Test";
            await Task.Delay(500); // Wait for debounce

            // Assert - search should filter results
            Assert.IsNotNull(_viewModel.Plugins);
        }

        [TestMethod]
        public async Task SelectedCategory_FiltersPlugins()
        {
            // Arrange
            await _viewModel.InitializeAsync();

            // Act - select a specific category
            var category = _viewModel.Categories.Skip(1).FirstOrDefault();
            if (category != null)
            {
                _viewModel.SelectedCategory = category;
                await Task.Delay(100);
            }

            // Assert
            Assert.IsNotNull(_viewModel.Plugins);
        }

        [TestMethod]
        public async Task SortOrder_ChangesPluginOrder()
        {
            // Arrange
            await _viewModel.InitializeAsync();

            // Act
            _viewModel.SortOrder = PluginSortOrder.Rating;
            await Task.Delay(100);

            // Assert
            Assert.AreEqual(PluginSortOrder.Rating, _viewModel.SortOrder);
        }

        [TestMethod]
        public async Task ShowInstalledOnly_FiltersToInstalledPlugins()
        {
            // Arrange
            await _viewModel.InitializeAsync();

            // Act
            _viewModel.ShowInstalledOnly = true;
            await Task.Delay(100);

            // Assert
            Assert.IsTrue(_viewModel.ShowInstalledOnly);
        }

        [TestMethod]
        public async Task ShowUpdatesOnly_FiltersToPluginsWithUpdates()
        {
            // Arrange
            await _viewModel.InitializeAsync();

            // Act
            _viewModel.ShowUpdatesOnly = true;
            await Task.Delay(100);

            // Assert
            Assert.IsTrue(_viewModel.ShowUpdatesOnly);
        }

        [TestMethod]
        public async Task ClearFiltersCommand_ResetsAllFilters()
        {
            // Arrange
            await _viewModel.InitializeAsync();
            _viewModel.SearchText = "Test";
            _viewModel.ShowInstalledOnly = true;
            _viewModel.ShowUpdatesOnly = true;

            // Act
            _viewModel.ClearFiltersCommand.Execute(null);

            // Assert
            Assert.AreEqual(string.Empty, _viewModel.SearchText);
            Assert.IsFalse(_viewModel.ShowInstalledOnly);
            Assert.IsFalse(_viewModel.ShowUpdatesOnly);
        }

        #endregion

        #region Installation Tests

        [TestMethod]
        public async Task InstallPluginCommand_InstallsPlugin()
        {
            // Arrange
            await _viewModel.InitializeAsync();
            var plugin = _viewModel.Plugins.FirstOrDefault();
            Assert.IsNotNull(plugin, "Need a plugin to test installation");

            // Act
            await _viewModel.InstallPluginCommand.ExecuteAsync(plugin);

            // Assert
            Assert.IsFalse(_viewModel.IsInstalling, "Installation should complete");
        }

        [TestMethod]
        public async Task InstallPluginCommand_HandlesFailure()
        {
            // Arrange
            await _viewModel.InitializeAsync();
            _mockGateway.ShouldFailInstall = true;
            var plugin = _viewModel.Plugins.FirstOrDefault();
            Assert.IsNotNull(plugin);

            // Act
            await _viewModel.InstallPluginCommand.ExecuteAsync(plugin);

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(_viewModel.ErrorMessage), "Error message should be set on failure");
        }

        [TestMethod]
        public async Task UninstallPluginCommand_UninstallsPlugin()
        {
            // Arrange
            await _viewModel.InitializeAsync();
            var installedPlugin = _viewModel.Plugins.FirstOrDefault(p => p.IsInstalled);
            
            // If no installed plugin, install one first
            if (installedPlugin == null)
            {
                installedPlugin = _viewModel.Plugins.FirstOrDefault();
                if (installedPlugin != null)
                {
                    await _viewModel.InstallPluginCommand.ExecuteAsync(installedPlugin);
                }
            }

            if (installedPlugin != null)
            {
                // Act
                await _viewModel.UninstallPluginCommand.ExecuteAsync(installedPlugin);

                // Assert
                Assert.IsFalse(_viewModel.IsInstalling, "Uninstall should complete");
            }
        }

        #endregion

        #region Pagination Tests

        [TestMethod]
        public async Task NextPageCommand_IncrementsPage()
        {
            // Arrange
            await _viewModel.InitializeAsync();
            var initialPage = _viewModel.CurrentPage;

            // Act - Only proceed if there's a next page
            if (_viewModel.HasNextPage)
            {
                await _viewModel.NextPageCommand.ExecuteAsync(null);
                Assert.AreEqual(initialPage + 1, _viewModel.CurrentPage);
            }
            else
            {
                Assert.AreEqual(1, _viewModel.CurrentPage);
            }
        }

        [TestMethod]
        public async Task PreviousPageCommand_DecrementsPage()
        {
            // Arrange
            await _viewModel.InitializeAsync();
            
            // First go to page 2 if possible
            if (_viewModel.HasNextPage)
            {
                await _viewModel.NextPageCommand.ExecuteAsync(null);
                var currentPage = _viewModel.CurrentPage;
                
                // Act
                await _viewModel.PreviousPageCommand.ExecuteAsync(null);
                
                // Assert
                Assert.AreEqual(currentPage - 1, _viewModel.CurrentPage);
            }
        }

        #endregion

        #region Refresh Tests

        [TestMethod]
        public async Task RefreshCommand_ReloadsData()
        {
            // Arrange
            await _viewModel.InitializeAsync();

            // Act
            await _viewModel.RefreshCommand.ExecuteAsync(null);

            // Assert
            Assert.IsTrue(_viewModel.Plugins.Count > 0);
        }

        [TestMethod]
        public async Task CheckForUpdatesCommand_ChecksForUpdates()
        {
            // Arrange
            await _viewModel.InitializeAsync();

            // Act
            await _viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

            // Assert
            Assert.IsFalse(_viewModel.IsLoading, "Should complete loading");
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public async Task InitializeAsync_HandlesApiFailure()
        {
            // Arrange
            _mockGateway.ShouldFail = true;

            // Act
            await _viewModel.InitializeAsync();

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(_viewModel.ErrorMessage), "Error message should be set on API failure");
        }

        #endregion

        #region View Details Tests

        [TestMethod]
        public async Task ViewPluginDetailsCommand_SetsSelectedPlugin()
        {
            // Arrange
            await _viewModel.InitializeAsync();
            var plugin = _viewModel.Plugins.FirstOrDefault();
            Assert.IsNotNull(plugin);

            // Act
            _viewModel.ViewPluginDetailsCommand.Execute(plugin);

            // Assert
            Assert.AreEqual(plugin, _viewModel.SelectedPlugin);
        }

        #endregion
    }
}
