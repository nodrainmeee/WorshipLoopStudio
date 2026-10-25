using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WorshipLoopStudio.App.ViewModels;

namespace WorshipLoopStudio.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Live shortcuts are disabled while typing in a text field or using a list/combo box.
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem or ListBoxItem or Slider)
        {
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            _vm.Perform.TogglePlay();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.Perform.Stop();
            e.Handled = true;
        }
        else if (e.Key is >= Key.D1 and <= Key.D9)
        {
            _vm.Perform.TriggerTrackByIndex(e.Key - Key.D1);
            e.Handled = true;
        }
        else if (e.Key is >= Key.NumPad1 and <= Key.NumPad9)
        {
            _vm.Perform.TriggerTrackByIndex(e.Key - Key.NumPad1);
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _vm.OnClosing(e);
}
