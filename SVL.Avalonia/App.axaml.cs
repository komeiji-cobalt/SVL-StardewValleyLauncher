using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SVL.Avalonia.Services;
using SVL.Avalonia.ViewModels;
using SVL.Avalonia.Views;

namespace SVL.Avalonia;

public partial class App : Application
{
    private static DebugConsoleWindow? s_debugConsoleWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new AppUserSettingsStore().Load();
            var enableDebugConsole = settings.DebugMode;

#if DEBUG
            enableDebugConsole = true;
#endif

            if (enableDebugConsole)
            {
                DebugTraceBootstrapper.Initialize();
            }

            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };

            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _ = ShowSplashThenMainAsync(desktop, mainWindow, enableDebugConsole);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowSplashThenMainAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        Window mainWindow,
        bool autoOpenDebugConsole)
    {
        var splash = new SplashWindow();

        try
        {
            desktop.MainWindow = splash;
            splash.Show();
            await Task.Delay(2000);
        }
        finally
        {
            if (splash.IsVisible)
            {
                splash.Close();
            }
        }

        desktop.MainWindow = mainWindow;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        await Dispatcher.UIThread.InvokeAsync(mainWindow.Show);

        if (autoOpenDebugConsole)
        {
            await Dispatcher.UIThread.InvokeAsync(() => OpenDebugConsole(mainWindow));
        }
    }

    private static void OpenDebugConsole(Window owner)
    {
        if (s_debugConsoleWindow is { IsVisible: true })
        {
            s_debugConsoleWindow.Activate();
            return;
        }

        var window = new DebugConsoleWindow
        {
            DataContext = new DebugConsoleViewModel(DebugConsoleService.Instance)
        };

        s_debugConsoleWindow = window;
        window.Closed += (_, _) => s_debugConsoleWindow = null;
        window.Show(owner);
        DebugConsoleService.Instance.Append("Debug console auto-opened at startup.");
    }
}
