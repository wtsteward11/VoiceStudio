using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private ListView _listView = null!;
    private AutoSuggestBox _searchBox = null!;
    private ComboBox _regionChooser = null!;
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
      mainStack.Children.Add(_listView);

      Content = mainStack;

      PrimaryButtonClick += (s, e) =>
      {
        var selected = _listView.SelectedItem as PanelDescriptor;
        if (selected != null)
        {
          SelectedDescriptor = selected;
          SelectedRegion = _regionChooser.SelectedItem as PanelRegion?;
        }
      };

      Loaded += (_, _) => LoadDescriptors();
    }

    private void LoadDescriptors()
    {
      _allDescriptors = _registry.GetAllDescriptors()
        .Where(d => d.Maturity != PanelMaturity.Deprecated)
        .OrderBy(d => d.MenuCategory ?? "Other")
        .ThenBy(d => d.DisplayName)
        .ToList();
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
      if (string.IsNullOrWhiteSpace(query))
      {
        _filteredDescriptors = _allDescriptors.ToList();
      }
      else
      {
        var q = query.Trim();
        _filteredDescriptors = _allDescriptors.Where(d =>
          (d.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
          (d.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
          (d.Keywords?.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? false) ||
          (d.PanelId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
          (d.MenuCategory?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
      }

      _listView.ItemsSource = null;
      _listView.ItemsSource = _filteredDescriptors;
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
        SelectedRegion = _regionChooser.SelectedItem as PanelRegion?;
        Hide();
      }
    }
  }
}
