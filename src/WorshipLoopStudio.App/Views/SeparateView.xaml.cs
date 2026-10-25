using System.Windows.Controls;

namespace WorshipLoopStudio.App.Views;

public partial class SeparateView : UserControl
{
    public SeparateView()
    {
        InitializeComponent();
    }

    private void OnLogChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.ScrollToEnd();
        }
    }
}
