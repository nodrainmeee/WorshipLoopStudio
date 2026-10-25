using System.Windows;
using System.Windows.Threading;
using WorshipLoopStudio.App.Services;
using WorshipLoopStudio.App.ViewModels;

namespace WorshipLoopStudio.App;

public partial class App : Application
{
    private AppServices? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _services?.Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _services?.Log.Warn("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
#pragma warning disable WPF0001
            ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
        }
        catch (Exception)
        {
            // Older runtimes without the Fluent theme keep the classic look with our dark colors.
        }

        try
        {
            _services = new AppServices();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Worship Loop Studio could not start:\n\n{ex.Message}", "Worship Loop Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var vm = new MainViewModel(_services);
        var window = new MainWindow(vm);
        MainWindow = window;
        window.Show();
        await vm.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // During a live service the app must keep running: log, show the banner, continue.
        _services?.Log.Error("UI exception", e.Exception);
        Infrastructure.ErrorReporter.Report("Unexpected error", e.Exception);
        e.Handled = true;
    }
}
