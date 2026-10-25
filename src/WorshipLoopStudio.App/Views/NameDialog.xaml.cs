using System.Windows;

namespace WorshipLoopStudio.App.Views;

public partial class NameDialog : Window
{
    public NameDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            ErrorText.Visibility = Visibility.Visible;
            ValueBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
