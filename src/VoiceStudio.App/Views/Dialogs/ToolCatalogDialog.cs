using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Dialogs
{
  /// <summary>
  /// Dialog for browsing and opening panels by ID. Search + category grouping.
  /// Code-behind only (no XAML). Opens via OpenPanelByIdAsync to target region.
  /// </summary>
  public sealed class ToolCatalogDialog : ContentDialog
  {
    private readonly IPanelRegistry _registry;
    private readonly PanelStateService _panelStateService;
    private ListView _listView = null!;
    private AutoSuggestBox _searchBox = null!;
    private ComboBox _regionChooser = null!;
    private ComboBox _categoryFilter = null!;
    private ComboBox _maturityFilter = null!;
    private List<PanelDescriptor> _allDescriptors = new();
    private List<PanelDescriptor> _filteredDescriptors = new();

    public PanelDescriptor? SelectedDescriptor { get; private set; }
    public PanelRegion? SelectedRegion { get; private set; }

    public ToolCatalogDialog(XamlRoot? xamlRoot = null)
    {
      Title = "Tool Catalog";
      PrimaryButtonText = "Open";
      SecondaryButtonText = "Cancel";
      DefaultButton = ContentDialogButton.Primary;
      XamlRoot = xamlRoot;

      _registry = AppServices.GetPanelRegistry();
      _panelStateService = AppServices.GetPanelStateService();

      var mainStack = new StackPanel { Spacing = 12 };

      _searchBox = new AutoSuggestBox
      {
        PlaceholderText = "Search panels...",
        Width = double.NaN,
        HorizontalAlignment = HorizontalAlignment.Stretch
      };
      _searchBox.TextChanged += SearchBox_TextChanged;
      _searchBox.QuerySubmitted += SearchBox_QuerySubmitted;
      mainStack.Children.Add(_searchBox);

      var filterStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
      _categoryFilter = new ComboBox
      {
        Header = "Category",
        Width = 140,
        HorizontalAlignment = HorizontalAlignment.Left
      };
      _categoryFilter.SelectionChanged += (_, _) => ApplyFilter(_searchBox.Text);
      filterStack.Children.Add(_categoryFilter);
      _maturityFilter = new ComboBox
      {
        Header = "Maturity",
        Width = 120,
        HorizontalAlignment = HorizontalAlignment.Left
      };
      _maturityFilter.SelectionChanged += (_, _) => ApplyFilter(_searchBox.Text);
      filterStack.Children.Add(_maturityFilter);
      mainStack.Children.Add(filterStack);

      _regionChooser = new ComboBox
      {
        Header = "Region",
        Width = double.NaN,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { PanelRegion.Left, PanelRegion.Center, PanelRegion.Right, PanelRegion.Bottom }
      };
      mainStack.Children.Add(_regionChooser);

      _listView = new ListView
      {
        SelectionMode = ListViewSelectionMode.Single,
        MinHeight = 200,
        MaxHeight = 400,
        IsItemClickEnabled = true,
        DisplayMemberPath = "DisplayName"
      };
      _listView.ItemClick += ListView_ItemClick;
      _listView.SelectionChanged += ListView_SelectionChanged;
      _listView.DoubleTapped += ListView_DoubleTapped;
      _listView.ContextRequested += ListView_ContextRequested;
      mainStack.Children.Add(_listView);

      Content = mainStack;

      PrimaryButtonClick += (s, e) =>
      {
        var selected = _listView.SelectedItem as PanelDescriptor;
        if (selected != null)
        {
          SelectedDescriptor = selected;
          SelectedRegion = _regionChooser.SelectedItem is PanelRegion pr ? pr : (PanelRegion?)null;
        }
      };

      Loaded += (_, _) => LoadDescriptors();
    }

    private void LoadDescriptors()
    {
      _allDescriptors = _registry.GetAllDescriptors()
        .Where(d => d.IsVisible)
        .Where(d => d.Maturity != PanelMaturity.Deprecated)
        .OrderBy(d => d.MenuCategory ?? "Other")
        .ThenBy(d => d.DisplayName)
        .ToList();

      var categories = new List<string> { "All" };
      categories.AddRange(_allDescriptors
        .Select(d => d.MenuCategory ?? "Other")
        .Distinct()
        .OrderBy(c => c));
      _categoryFilter.ItemsSource = categories;
      _categoryFilter.SelectedIndex = 0;

      _maturityFilter.ItemsSource = new[] { "All", "Stable", "Beta", "Experimental" };
      _maturityFilter.SelectedIndex = 0;

      ApplyFilter(_searchBox.Text);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
      if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
      {
        ApplyFilter(sender.Text);
      }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
      ApplyFilter(args.QueryText);
    }

    private void ApplyFilter(string? query)
    {
      var filtered = _allDescriptors.AsEnumerable();

      var categorySel = _categoryFilter.SelectedItem as string;
      if (!string.IsNullOrEmpty(categorySel) && categorySel != "All")
        filtered = filtered.Where(d => (d.MenuCategory ?? "Other") == categorySel);

      var maturitySel = _maturityFilter.SelectedItem as string;
      if (!string.IsNullOrEmpty(maturitySel) && maturitySel != "All")
      {
        var maturity = maturitySel switch
        {
          "Stable" => PanelMaturity.Stable,
          "Beta" => PanelMaturity.Beta,
          "Experimental" => PanelMaturity.Experimental,
          _ => (PanelMaturity?)null
        };
        if (maturity.HasValue)
          filtered = filtered.Where(d => d.Maturity == maturity.Value);
      }

      if (!string.IsNullOrWhiteSpace(query))
      {
        var q = query.Trim();
        filtered = filtered.Where(d =>
          (d.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
          (d.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
          (d.Keywords?.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? false) ||
          (d.PanelId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
          (d.MenuCategory?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
      }

      _filteredDescriptors = filtered
        .OrderByDescending(d => _panelStateService.IsPanelPinned(d.PanelId ?? string.Empty))
        .ThenBy(d => d.MenuCategory ?? "Other")
        .ThenBy(d => d.DisplayName)
        .ToList();
      _listView.ItemsSource = null;
      _listView.ItemsSource = _filteredDescriptors;
    }

    private void ListView_ContextRequested(object sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e)
    {
      var listView = (ListView)sender;

      var element = e.OriginalSource as DependencyObject;
      while (element != null && element is not ListViewItem)
        element = VisualTreeHelper.GetParent(element);
      if (element is ListViewItem lvi)
        listView.SelectedIndex = listView.IndexFromContainer(lvi);

      var item = listView.SelectedItem as PanelDescriptor;
      if (item?.PanelId == null) return;

      var flyout = new MenuFlyout();
      var isPinned = _panelStateService.IsPanelPinned(item.PanelId);
      var pinItem = new MenuFlyoutItem
      {
        Text = isPinned ? "Unpin" : "Pin to top"
      };
      pinItem.Click += (_, _) =>
      {
        _panelStateService.TogglePinnedPanel(item.PanelId);
        ApplyFilter(_searchBox.Text);
      };
      flyout.Items.Add(pinItem);
      if (e.TryGetPosition(listView, out var point))
        flyout.ShowAt(listView, point);
      else
        flyout.ShowAt(listView);
    }

    private void ListView_ItemClick(object sender, ItemClickEventArgs e)
    {
      if (e.ClickedItem is PanelDescriptor desc)
      {
        _listView.SelectedItem = desc;
      }
    }

    private void ListView_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
      if (_listView.SelectedItem is PanelDescriptor desc)
      {
        var region = desc.DefaultRegion is PanelRegion.Floating ? PanelRegion.Center : desc.DefaultRegion;
        _regionChooser.SelectedItem = region;
      }
    }

    private void ListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
      if (_listView.SelectedItem is PanelDescriptor desc)
      {
        SelectedDescriptor = desc;
        SelectedRegion = _regionChooser.SelectedItem is PanelRegion pr2 ? pr2 : (PanelRegion?)null;
        Hide();
      }
    }
  }
}
