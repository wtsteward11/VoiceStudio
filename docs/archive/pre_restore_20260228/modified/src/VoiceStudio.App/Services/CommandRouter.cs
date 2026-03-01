using System;
using VoiceStudio.App.Logging;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using VoiceStudio.App.Core.Commands;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Routes UI events to commands in the UnifiedCommandRegistry.
    /// Provides a bridge between XAML UI elements and the command system.
    /// </summary>
    public sealed class CommandRouter
    {
        private readonly IUnifiedCommandRegistry _registry;
        private readonly DispatcherQueue? _dispatcherQueue;

        public CommandRouter(IUnifiedCommandRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }

        #region Direct Execution

        /// <summary>
        /// Executes a command by ID with optional parameter.
        /// </summary>
        public Task ExecuteAsync(string commandId, object? parameter = null, CancellationToken cancellationToken = default)
        {
            return _registry.ExecuteAsync(commandId, parameter, cancellationToken);
        }

        /// <summary>
        /// Executes a command by ID, fire-and-forget style.
        /// Errors are logged but not propagated.
        /// Ensures execution on the UI thread for COM operations like file pickers.
        /// </summary>
        public void ExecuteFireAndForget(string commandId, object? parameter = null)
        {
            // Ensure execution on UI thread for commands that may use COM objects (file pickers, dialogs)
            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                _dispatcherQueue.TryEnqueue(() => _ = ExecuteSafeAsync(commandId, parameter));
            }
            else
            {
                _ = ExecuteSafeAsync(commandId, parameter);
            }
        }

        /// <summary>
        /// Executes a command safely, catching and logging any exceptions.
        /// </summary>
        public async Task<bool> ExecuteSafeAsync(string commandId, object? parameter = null, CancellationToken cancellationToken = default)
        {
            try
            {
                await _registry.ExecuteAsync(commandId, parameter, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"[CommandRouter] Command '{commandId}' failed: {ex.Message}", "CommandRouter");
                return false;
            }
        }

        /// <summary>
        /// Checks if a command can execute.
        /// </summary>
        public bool CanExecute(string commandId, object? parameter = null)
        {
            return _registry.CanExecute(commandId, parameter);
        }

        #endregion

        #region Event Handler Factories

        /// <summary>
        /// Creates a RoutedEventHandler that executes the specified command.
        /// Suitable for Button.Click events.
        /// </summary>
        public RoutedEventHandler CreateClickHandler(string commandId, object? parameter = null)
        {
            return (sender, args) => ExecuteFireAndForget(commandId, parameter);
        }

        /// <summary>
        /// Creates a RoutedEventHandler that extracts parameter from the sender's Tag property.
        /// </summary>
        public RoutedEventHandler CreateClickHandlerWithTag(string commandId)
        {
            return (sender, args) =>
            {
                var tag = (sender as FrameworkElement)?.Tag;
                ExecuteFireAndForget(commandId, tag);
            };
        }

        /// <summary>
        /// Creates a RoutedEventHandler that extracts parameter from the sender's DataContext.
        /// </summary>
        public RoutedEventHandler CreateClickHandlerWithDataContext(string commandId)
        {
            return (sender, args) =>
            {
                var context = (sender as FrameworkElement)?.DataContext;
                ExecuteFireAndForget(commandId, context);
            };
        }

        /// <summary>
        /// Creates a SelectionChangedEventHandler that passes the selected item.
        /// </summary>
        public SelectionChangedEventHandler CreateSelectionHandler(string commandId)
        {
            return (sender, args) =>
            {
                if (args.AddedItems.Count > 0)
                {
                    ExecuteFireAndForget(commandId, args.AddedItems[0]);
                }
            };
        }

        /// <summary>
        /// Creates a TypedEventHandler for toggle buttons that passes the toggle state.
        /// </summary>
        public RoutedEventHandler CreateToggleHandler(string commandId)
        {
            return (sender, args) =>
            {
                var isChecked = (sender as ToggleButton)?.IsChecked ?? false;
                ExecuteFireAndForget(commandId, isChecked);
            };
        }

        #endregion

        #region UI Element Wiring

        /// <summary>
        /// Wires a button to execute a command on click.
        /// </summary>
        public void WireButton(ButtonBase button, string commandId, object? parameter = null)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));

            // Use the registry's ICommand wrapper for proper CanExecute binding
            var command = _registry.GetCommand(commandId);
            if (command != null)
            {
                button.Command = command;
                button.CommandParameter = parameter;
            }
            else
            {
                // Fallback to click handler if command not found
                button.Click += CreateClickHandler(commandId, parameter);
                ErrorLogger.LogDebug($"[CommandRouter] Warning: Command '{commandId}' not found, using click handler fallback", "CommandRouter");
            }
        }

        /// <summary>
        /// Wires a menu flyout item to execute a command.
        /// </summary>
        public void WireMenuItem(MenuFlyoutItem menuItem, string commandId, object? parameter = null)
        {
            if (menuItem == null) throw new ArgumentNullException(nameof(menuItem));

            var command = _registry.GetCommand(commandId);
            if (command != null)
            {
                menuItem.Command = command;
                menuItem.CommandParameter = parameter;
            }
            else
            {
                menuItem.Click += CreateClickHandler(commandId, parameter);
                ErrorLogger.LogDebug($"[CommandRouter] Warning: Command '{commandId}' not found for menu item", "CommandRouter");
            }
        }

        /// <summary>
        /// Wires a toggle button to execute a command with the toggle state.
        /// </summary>
        public void WireToggle(ToggleButton toggle, string commandId)
        {
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));

            toggle.Checked += CreateToggleHandler(commandId);
            toggle.Unchecked += CreateToggleHandler(commandId);
        }

        #endregion

        #region Batch Operations

        /// <summary>
        /// Wires multiple buttons to their respective commands based on Tag values.
        /// Each button's Tag should contain the command ID.
        /// </summary>
        /// <remarks>
        /// GAP-B19: Tag values are validated against CommandIds for compile-time safety.
        /// </remarks>
        public void WireButtonsByTag(params ButtonBase[] buttons)
        {
            foreach (var button in buttons)
            {
                if (button?.Tag is string commandId && !string.IsNullOrEmpty(commandId))
                {
                    // GAP-B19: Validate Tag against known command IDs
                    if (!CommandIds.IsKnown(commandId))
                    {
                        ErrorLogger.LogDebug($"[CommandRouter] WARNING: Button Tag '{commandId}' is not a known command ID. " +
                            $"Add it to CommandIds.cs or fix the Tag value.", "CommandRouter");
                    }
                    WireButton(button, commandId);
                }
            }
        }

        /// <summary>
        /// Wires multiple menu items to their respective commands based on Tag values.
        /// </summary>
        /// <remarks>
        /// GAP-B19: Tag values are validated against CommandIds for compile-time safety.
        /// </remarks>
        public void WireMenuItemsByTag(params MenuFlyoutItem[] menuItems)
        {
            foreach (var item in menuItems)
            {
                if (item?.Tag is string commandId && !string.IsNullOrEmpty(commandId))
                {
                    // GAP-B19: Validate Tag against known command IDs
                    if (!CommandIds.IsKnown(commandId))
                    {
                        ErrorLogger.LogDebug($"[CommandRouter] WARNING: MenuItem Tag '{commandId}' is not a known command ID. " +
                            $"Add it to CommandIds.cs or fix the Tag value.", "CommandRouter");
                    }
                    WireMenuItem(item, commandId);
                }
            }
        }

        #endregion

        #region UI Thread Safety

        /// <summary>
        /// Executes a command on the UI thread.
        /// </summary>
        public void ExecuteOnUIThread(string commandId, object? parameter = null)
        {
            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                _dispatcherQueue.TryEnqueue(() => ExecuteFireAndForget(commandId, parameter));
            }
            else
            {
                ExecuteFireAndForget(commandId, parameter);
            }
        }

        #endregion
    }
}
