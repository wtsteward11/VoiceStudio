using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Controls
{
    /// <summary>
    /// Helper for configuring UI virtualization on list controls.
    /// Provides optimal virtualization settings for large data sets.
    ///
    /// Usage:
    /// 1. For ListView: VirtualizedListHelper.ConfigureListView(listView)
    /// 2. For ItemsRepeater: Use VirtualizedListHelper.CreateVirtualizingLayout()
    /// </summary>
    public static class VirtualizedListHelper
    {
        /// <summary>
        /// Default page size for incremental loading.
        /// </summary>
        public const int DefaultPageSize = 50;

        /// <summary>
        /// Buffer size for item recycling.
        /// </summary>
        public const int DefaultRecycleBuffer = 10;

        /// <summary>
        /// XAML template for ItemsStackPanel with optimal virtualization settings.
        /// CacheLength of 4.0 provides smooth scrolling with reasonable memory usage.
        /// </summary>
        private const string ItemsStackPanelTemplate = @"
            <ItemsPanelTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <ItemsStackPanel Orientation=""Vertical"" CacheLength=""4"" />
            </ItemsPanelTemplate>";

        /// <summary>
        /// XAML template for horizontal ItemsStackPanel.
        /// </summary>
        private const string HorizontalItemsStackPanelTemplate = @"
            <ItemsPanelTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <ItemsStackPanel Orientation=""Horizontal"" CacheLength=""4"" />
            </ItemsPanelTemplate>";

        /// <summary>
        /// Configures a ListView for optimal virtualization performance.
        /// </summary>
        /// <param name="listView">The ListView to configure.</param>
        /// <param name="horizontal">If true, uses horizontal orientation.</param>
        public static void ConfigureListView(ListView listView, bool horizontal = false)
        {
            if (listView == null)
                return;

            // Enable item click for selection
            listView.IsItemClickEnabled = true;

            // Create ItemsPanelTemplate with virtualized ItemsStackPanel using XamlReader
            // This ensures proper virtualization with optimal cache settings
            try
            {
                var template = horizontal ? HorizontalItemsStackPanelTemplate : ItemsStackPanelTemplate;
                listView.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load(template);
            }
            catch (Exception ex)
            {
                // Log and fall back to default template (still virtualized, just with default cache)
                System.Diagnostics.Debug.WriteLine($"[VirtualizedListHelper] Failed to configure ListView ItemsPanel: {ex.Message}");
            }

            // Performance notes for large lists:
            // - Use x:Load for conditional loading in item templates
            // - Use x:Phase for phased loading of complex items
            // - Keep item templates simple and avoid deep nesting
            // - Consider IncrementalLoadingCollection for very large datasets
        }

        /// <summary>
        /// Configures a GridView for optimal virtualization performance.
        /// </summary>
        /// <param name="gridView">The GridView to configure.</param>
        public static void ConfigureGridView(GridView gridView)
        {
            if (gridView == null)
                return;

            // GridView uses ItemsWrapGrid by default which supports virtualization
            // We configure it for optimal performance
            try
            {
                const string gridTemplate = @"
                    <ItemsPanelTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                        <ItemsWrapGrid Orientation=""Horizontal"" CacheLength=""4"" />
                    </ItemsPanelTemplate>";
                gridView.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load(gridTemplate);
            }
            catch (Exception ex)
            {
                // Log but continue - GridView will use its default ItemsPanel
                System.Diagnostics.Debug.WriteLine($"[VirtualizedListHelper] Failed to configure GridView ItemsPanel: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a virtualizing stack layout for ItemsRepeater.
        /// </summary>
        public static StackLayout CreateVirtualizingLayout(Orientation orientation = Orientation.Vertical)
        {
            return new StackLayout
            {
                Orientation = orientation,
                Spacing = 4,
            };
        }

        /// <summary>
        /// Creates an optimized uniform grid layout for ItemsRepeater.
        /// Good for grids of similarly-sized items (thumbnails, cards).
        /// </summary>
        public static UniformGridLayout CreateGridLayout(
            int minItemWidth = 200,
            int minItemHeight = 150,
            int minColumnSpacing = 8,
            int minRowSpacing = 8)
        {
            return new UniformGridLayout
            {
                MinItemWidth = minItemWidth,
                MinItemHeight = minItemHeight,
                MinColumnSpacing = minColumnSpacing,
                MinRowSpacing = minRowSpacing,
                ItemsStretch = UniformGridLayoutItemsStretch.Fill,
                ItemsJustification = UniformGridLayoutItemsJustification.Start,
            };
        }

        // NOTE: FlowLayout is not available in the current Windows App SDK version.
        // Use StackLayout or UniformGridLayout instead.
        // This method is commented out until FlowLayout becomes available.
        //
        // public static FlowLayout CreateFlowLayout(
        //     Orientation orientation = Orientation.Horizontal,
        //     int minColumnSpacing = 8,
        //     int minRowSpacing = 8)
        // {
        //     return new FlowLayout
        //     {
        //         Orientation = orientation,
        //         MinColumnSpacing = minColumnSpacing,
        //         MinRowSpacing = minRowSpacing,
        //     };
        // }
    }

    /// <summary>
    /// Incremental loading source for virtualized lists.
    /// Implements paging for large data sets.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    public class IncrementalLoadingCollection<T> :
        System.Collections.ObjectModel.ObservableCollection<T>,
        Microsoft.UI.Xaml.Data.ISupportIncrementalLoading
    {
        private readonly Func<int, int, System.Threading.Tasks.Task<IList<T>>> _loadMoreAsync;
        private readonly int _pageSize;
        private bool _hasMoreItems = true;

        /// <summary>
        /// Creates a new incremental loading collection.
        /// </summary>
        /// <param name="loadMoreAsync">Function to load more items (page, pageSize) => items</param>
        /// <param name="pageSize">Number of items per page</param>
        public IncrementalLoadingCollection(
            Func<int, int, System.Threading.Tasks.Task<IList<T>>> loadMoreAsync,
            int pageSize = VirtualizedListHelper.DefaultPageSize)
        {
            _loadMoreAsync = loadMoreAsync;
            _pageSize = pageSize;
        }

        public bool HasMoreItems => _hasMoreItems;

        public Windows.Foundation.IAsyncOperation<Microsoft.UI.Xaml.Data.LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            return LoadMoreItemsCoreAsync(count).AsAsyncOperation();
        }

        private async System.Threading.Tasks.Task<Microsoft.UI.Xaml.Data.LoadMoreItemsResult> LoadMoreItemsCoreAsync(uint count)
        {
            try
            {
                var currentPage = Count / _pageSize;
                var items = await _loadMoreAsync(currentPage, _pageSize);

                if (items == null || items.Count == 0)
                {
                    _hasMoreItems = false;
                    return new Microsoft.UI.Xaml.Data.LoadMoreItemsResult { Count = 0 };
                }

                if (items.Count < _pageSize)
                {
                    _hasMoreItems = false;
                }

                foreach (var item in items)
                {
                    Add(item);
                }

                return new Microsoft.UI.Xaml.Data.LoadMoreItemsResult { Count = (uint)items.Count };
            }
            catch
            {
                _hasMoreItems = false;
                return new Microsoft.UI.Xaml.Data.LoadMoreItemsResult { Count = 0 };
            }
        }
    }
}
