using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Panels
{
  /// <summary>
  /// Panel category for grouping in menus and Command Palette.
  /// </summary>
  public enum PanelCategory
  {
    General,
    Voice,
    Training,
    Audio,
    Settings,
    Diagnostics,
    Library,
    Effects,
    Automation,
    Other
  }

  /// <summary>
  /// Panel maturity/stability level.
  /// </summary>
  public enum PanelMaturity
  {
    Stable,
    Beta,
    Experimental,
    Deprecated
  }

  /// <summary>
  /// Describes a panel that can be registered and displayed in the UI.
  /// </summary>
  public sealed class PanelDescriptor
  {
    public string PanelId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public PanelRegion DefaultRegion { get; init; }
    public Type ViewType { get; init; } = typeof(object);
    public Type? ViewModelType { get; init; }
    public string? Icon { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Backward compatibility alias for DefaultRegion.
    /// </summary>
    public PanelRegion Region
    {
      get => DefaultRegion;
      init => DefaultRegion = value;
    }

    /// <summary>
    /// Category for grouping in menus and Command Palette.
    /// </summary>
    public PanelCategory Category { get; init; } = PanelCategory.General;

    /// <summary>
    /// Menu category for Modules menu grouping (Voice, Audio, Analysis, Media, Training, Editing, Automation, Management, System).
    /// When null, panel appears in "Other" group.
    /// </summary>
    public string? MenuCategory { get; init; }

    /// <summary>
    /// Stability level.
    /// </summary>
    public PanelMaturity Maturity { get; init; } = PanelMaturity.Stable;

    /// <summary>
    /// Search/filter terms for Command Palette. Optional; null by default.
    /// </summary>
    public IReadOnlyList<string>? Keywords { get; init; }
  }
}