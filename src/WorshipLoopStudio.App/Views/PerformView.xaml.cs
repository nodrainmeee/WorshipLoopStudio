using System.Windows.Controls;
using System.Windows.Input;
using WorshipLoopStudio.App.ViewModels;
using WorshipLoopStudio.Core.Samples;

namespace WorshipLoopStudio.App.Views;

public partial class PerformView : UserControl
{
    public PerformView()
    {
        InitializeComponent();
    }

    private void OnBpmKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnSampleDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PerformViewModel vm && vm.SelectedSample is SampleEntry sample && vm.PreviewCommand.CanExecute(sample))
        {
            vm.PreviewCommand.Execute(sample);
        }
    }
}
