using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Plugins;
using VoiceStudio.Core.Services;
using CoreModels = VoiceStudio.Core.Plugins.Models;

namespace VoiceStudio.App.Views.Panels
{
    /// <summary>
    /// Plugin Gallery panel for browsing and installing plugins.
    /// </summary>
    public sealed partial class PluginGalleryView : UserControl
    {
        private readonly PluginGalleryViewModel _viewModel;

        public PluginGalleryView()
        {
            this.InitializeComponent();

            // Create gateway and viewmodel
            var httpClient = new System.Net.Http.HttpClient();
            var gateway = new PluginGateway(httpClient);
            var context = AppServices.GetRequiredService<IViewModelContext>();
            _viewModel = new PluginGalleryViewModel(context, gateway);

            // Set DataContext for binding
            this.DataContext = _viewModel;

            // Bind to viewmodel changes
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Initialize
            _ = InitializeAsync();
        }

        public PluginGalleryView(IViewModelContext context, IPluginGateway gateway)
        {
            this.InitializeComponent();
            _viewModel = new PluginGalleryViewModel(context, gateway);
            this.DataContext = _viewModel;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _ = InitializeAsync();
        }

        public PluginGalleryViewModel ViewModel => _viewModel;

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                LoadingRing.IsActive = true;
                await _viewModel.InitializeAsync();
                
                // Set ItemsSource for GridViews
                FeaturedGridView.ItemsSource = _viewModel.FeaturedPlugins;
                PluginsGridView.ItemsSource = _viewModel.Plugins;
                
                // Populate category combo
                CategoryCombo.ItemsSource = _viewModel.Categories;
                if (_viewModel.Categories.Count > 0)
                {
                    CategoryCombo.SelectedIndex = 0;
                }

                // Set default sort
                SortCombo.SelectedIndex = 0;

                UpdateUI();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(_viewModel.IsLoading):
                        LoadingRing.IsActive = _viewModel.IsLoading;
                        break;

                    case nameof(_viewModel.IsInstalling):
                        InstallProgressOverlay.Visibility = _viewModel.IsInstalling 
                            ? Visibility.Visible 
                            : Visibility.Collapsed;
                        break;

                    case nameof(_viewModel.InstallProgress):
                        InstallProgressBar.Value = _viewModel.InstallProgress;
                        break;

                    case nameof(_viewModel.InstallStatusText):
                        InstallStatusText.Text = _viewModel.InstallStatusText;
                        break;

                    case nameof(_viewModel.StatusMessage):
                        StatusText.Text = _viewModel.StatusMessage;
                        break;

                    case nameof(_viewModel.ErrorMessage):
                        if (!string.IsNullOrEmpty(_viewModel.ErrorMessage))
                        {
                            ShowError(_viewModel.ErrorMessage);
                        }
                        break;

                    case nameof(_viewModel.TotalPlugins):
                    case nameof(_viewModel.CurrentPage):
                    case nameof(_viewModel.TotalPages):
                    case nameof(_viewModel.HasNextPage):
                    case nameof(_viewModel.HasPreviousPage):
                        UpdatePagination();
                        break;

                    case nameof(_viewModel.Plugins):
                        UpdateEmptyState();
                        break;
                }
            });
        }

        private void UpdateUI()
        {
            UpdatePagination();
            UpdateEmptyState();
        }

        private void UpdatePagination()
        {
            ResultCountText.Text = $"{_viewModel.TotalPlugins} plugins";
            PageText.Text = $"Page {_viewModel.CurrentPage} of {_viewModel.TotalPages}";
            PrevPageButton.IsEnabled = _viewModel.HasPreviousPage;
            NextPageButton.IsEnabled = _viewModel.HasNextPage;
        }

        private void UpdateEmptyState()
        {
            bool isEmpty = _viewModel.Plugins.Count == 0 && !_viewModel.IsLoading;
            EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            PluginScrollViewer.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void ShowError(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        #region Event Handlers

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _viewModel.SearchText = SearchBox.Text;
        }

        private void CategoryCombo_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (CategoryCombo.SelectedItem is CoreModels.PluginCategory category)
            {
                _viewModel.SelectedCategory = category;
            }
        }

        private void SortCombo_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string sortTag)
            {
                if (Enum.TryParse<CoreModels.PluginSortOrder>(sortTag, out var sortOrder))
                {
                    _viewModel.SortOrder = sortOrder;
                }
            }
        }

        private void FilterCheck_Changed(object sender, RoutedEventArgs e)
        {
            _viewModel.ShowInstalledOnly = InstalledOnlyCheck.IsChecked ?? false;
            _viewModel.ShowUpdatesOnly = UpdatesOnlyCheck.IsChecked ?? false;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.RefreshCommand.ExecuteAsync(null);
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        }

        private void FeaturedGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CoreModels.PluginInfo plugin)
            {
                NavigateToPluginDetails(plugin);
            }
        }

        private void PluginsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CoreModels.PluginInfo plugin)
            {
                NavigateToPluginDetails(plugin);
            }
        }

        private void NavigateToPluginDetails(CoreModels.PluginInfo plugin)
        {
            _viewModel.SelectedPlugin = plugin;
            
            // Navigate to detail view
            // This would typically use a navigation service
            System.Diagnostics.Debug.WriteLine($"[PluginGallery] Navigate to details: {plugin.Name}");
        }

        private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.PreviousPageCommand.ExecuteAsync(null);
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.NextPageCommand.ExecuteAsync(null);
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearFiltersCommand.Execute(null);
            SearchBox.Text = string.Empty;
            CategoryCombo.SelectedIndex = 0;
            SortCombo.SelectedIndex = 0;
            InstalledOnlyCheck.IsChecked = false;
            UpdatesOnlyCheck.IsChecked = false;
        }

        private void CancelInstall_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PluginGalleryViewModel viewModel)
            {
                viewModel.CancelInstallCommand.Execute(null);
            }
            InstallProgressOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion
    }
}
