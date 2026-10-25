using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using WorshipLoopStudio.App.Views;

namespace WorshipLoopStudio.App.Services;

public static class Dialogs
{
    private static Window? Owner => Application.Current?.MainWindow;

    public static string? OpenFile(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return ShowCommon(dialog) == true ? dialog.FileName : null;
    }

    public static string[] OpenFiles(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = true };
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return ShowCommon(dialog) == true ? dialog.FileNames : Array.Empty<string>();
    }

    public static string? SaveFile(string title, string filter, string fileName, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, AddExtension = true, OverwritePrompt = true };
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return ShowCommon(dialog) == true ? dialog.FileName : null;
    }

    public static string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return ShowCommon(dialog) == true ? dialog.FolderName : null;
    }

    private static bool? ShowCommon(CommonDialog dialog)
    {
        var owner = Owner;
        return owner is { IsLoaded: true } ? dialog.ShowDialog(owner) : dialog.ShowDialog();
    }

    public static string? AskText(string title, string prompt, string initial)
    {
        var dialog = new NameDialog(title, prompt, initial) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public static bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>Yes = save, No = discard, Cancel = abort.</summary>
    public static MessageBoxResult AskSaveChanges(string projectName) =>
        Show($"Save changes to '{projectName}'?", "Worship Loop Studio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    public static void Info(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Owner;
        return owner is { IsLoaded: true }
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
    }

    public static void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // Explorer unavailable: nothing else to do.
        }
    }

    public static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Ignore.
        }
    }
}
