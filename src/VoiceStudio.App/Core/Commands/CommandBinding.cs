// Copyright (c) 2024 VoiceStudio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml;

namespace VoiceStudio.App.Core.Commands;

/// <summary>
/// Attached properties for strongly-typed command binding in XAML.
/// Replaces fragile string Tag-based routing with type-safe attached properties.
/// </summary>
/// <remarks>
/// Phase 2.4: Eliminate Tag-based routing fragility (GAP-B20).
/// 
/// Usage in XAML:
/// <code>
/// &lt;Button local:CommandBinding.CommandId="synthesis.generate" /&gt;
/// </code>
/// </remarks>
public static class CommandBinding
{
    /// <summary>
    /// Identifies the CommandId attached property.
    /// </summary>
    public static readonly DependencyProperty CommandIdProperty =
        DependencyProperty.RegisterAttached(
            "CommandId",
            typeof(string),
            typeof(CommandBinding),
            new PropertyMetadata(null, OnCommandIdChanged));

    /// <summary>
    /// Gets the command ID for the specified element.
    /// </summary>
    /// <param name="obj">The element to get the command ID from.</param>
    /// <returns>The command ID string.</returns>
    public static string GetCommandId(DependencyObject obj)
    {
        return (string)obj.GetValue(CommandIdProperty);
    }

    /// <summary>
    /// Sets the command ID for the specified element.
    /// </summary>
    /// <param name="obj">The element to set the command ID on.</param>
    /// <param name="value">The command ID string.</param>
    public static void SetCommandId(DependencyObject obj, string value)
    {
        obj.SetValue(CommandIdProperty, value);
    }

    /// <summary>
    /// Identifies the CommandParameter attached property.
    /// </summary>
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.RegisterAttached(
            "CommandParameter",
            typeof(object),
            typeof(CommandBinding),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the command parameter for the specified element.
    /// </summary>
    /// <param name="obj">The element to get the parameter from.</param>
    /// <returns>The command parameter object.</returns>
    public static object? GetCommandParameter(DependencyObject obj)
    {
        return obj.GetValue(CommandParameterProperty);
    }

    /// <summary>
    /// Sets the command parameter for the specified element.
    /// </summary>
    /// <param name="obj">The element to set the parameter on.</param>
    /// <param name="value">The command parameter object.</param>
    public static void SetCommandParameter(DependencyObject obj, object? value)
    {
        obj.SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Identifies the AutoWireCommand attached property.
    /// When true, automatically wires the element's Click event to the command.
    /// </summary>
    public static readonly DependencyProperty AutoWireCommandProperty =
        DependencyProperty.RegisterAttached(
            "AutoWireCommand",
            typeof(bool),
            typeof(CommandBinding),
            new PropertyMetadata(false, OnAutoWireChanged));

    /// <summary>
    /// Gets whether auto-wiring is enabled for the specified element.
    /// </summary>
    public static bool GetAutoWireCommand(DependencyObject obj)
    {
        return (bool)obj.GetValue(AutoWireCommandProperty);
    }

    /// <summary>
    /// Sets whether auto-wiring is enabled for the specified element.
    /// </summary>
    public static void SetAutoWireCommand(DependencyObject obj, bool value)
    {
        obj.SetValue(AutoWireCommandProperty, value);
    }

    private static void OnCommandIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Command ID has changed - if auto-wire is enabled, update the binding
        if (GetAutoWireCommand(d) && d is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button)
        {
            WireButtonToCommand(button, e.NewValue as string);
        }
    }

    private static void OnAutoWireChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button)
        {
            var commandId = GetCommandId(button);
            if ((bool)e.NewValue && !string.IsNullOrEmpty(commandId))
            {
                WireButtonToCommand(button, commandId);
            }
            else if (!(bool)e.NewValue)
            {
                UnwireButton(button);
            }
        }
    }

    private static void WireButtonToCommand(
        Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button,
        string? commandId)
    {
        if (string.IsNullOrEmpty(commandId))
            return;

        // Get the command from the registry
        var registry = Services.AppServices.TryGetCommandRegistry();
        if (registry == null)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CommandBinding] Cannot wire '{commandId}' - registry not available");
            return;
        }

        var command = registry.GetCommand(commandId);
        if (command != null)
        {
            button.Command = command;
            button.CommandParameter = GetCommandParameter(button);
            System.Diagnostics.Debug.WriteLine(
                $"[CommandBinding] Wired '{commandId}' to button");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CommandBinding] Command '{commandId}' not found in registry");
        }
    }

    private static void UnwireButton(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button)
    {
        button.Command = null;
        button.CommandParameter = null;
    }
}
