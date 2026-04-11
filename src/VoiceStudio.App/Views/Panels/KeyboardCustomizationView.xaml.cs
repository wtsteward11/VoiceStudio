using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace VoiceStudio.App.Views.Panels
{
    public sealed partial class KeyboardCustomizationView : UserControl
    {
        public KeyboardCustomizationView()
        {
            InitializeComponent();
        }

        private KeyboardCustomizationViewModel? ViewModel => DataContext as KeyboardCustomizationViewModel;

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string id })
            {
                return;
            }

            var vm = ViewModel;
            if (vm == null)
            {
                return;
            }

            vm.StartEditCommand.Execute(id);
            ChordCaptureBox.Focus(FocusState.Programmatic);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string id })
            {
                return;
            }

            ViewModel?.ResetBindingCommand.Execute(id);
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.CancelEditCommand.Execute(null);
        }

        private async void ResetAll_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            await ViewModel.ResetAllCommand.ExecuteAsync(null);
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpOverlay.Title = "Keyboard Shortcuts";
            HelpOverlay.HelpText =
                "Select a row, then click Edit and press your desired key combination. " +
                "Conflicts are highlighted in red. Use Reset to restore the default binding. " +
                "Press Escape to cancel an active capture.";
            HelpOverlay.Show();
        }

        private async void ChordCaptureBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null || string.IsNullOrEmpty(vm.EditingCommandId))
            {
                return;
            }

            if (e.Key == VirtualKey.Escape)
            {
                vm.CancelEditCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (IsModifierVirtualKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            var mods = GetCurrentModifiers();
            var commandId = vm.EditingCommandId!;
            await vm.CommitChordAsync(commandId, e.Key, mods).ConfigureAwait(true);
            e.Handled = true;
        }

        private static bool IsModifierVirtualKey(VirtualKey key)
        {
            return key is VirtualKey.Control
                or VirtualKey.LeftControl
                or VirtualKey.RightControl
                or VirtualKey.Shift
                or VirtualKey.LeftShift
                or VirtualKey.RightShift
                or VirtualKey.Menu
                or VirtualKey.LeftMenu
                or VirtualKey.RightMenu
                or VirtualKey.LeftWindows
                or VirtualKey.RightWindows;
        }

        private static VirtualKeyModifiers GetCurrentModifiers()
        {
            var m = VirtualKeyModifiers.None;
            if (IsKeyDown(VirtualKey.Control))
            {
                m |= VirtualKeyModifiers.Control;
            }

            if (IsKeyDown(VirtualKey.Shift))
            {
                m |= VirtualKeyModifiers.Shift;
            }

            if (IsKeyDown(VirtualKey.Menu))
            {
                m |= VirtualKeyModifiers.Menu;
            }

            if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
            {
                m |= VirtualKeyModifiers.Windows;
            }

            return m;
        }

        private static bool IsKeyDown(VirtualKey key)
        {
            var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
            return state.HasFlag(CoreVirtualKeyStates.Down);
        }
    }
}
