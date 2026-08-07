using Cinder.MINT.Controls;
using System.Windows;
using System.Windows.Threading;

namespace Cinder.MINT;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Loaded += (_, _) => MintyControlDeckHost.Attach(window);
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"MintyFilter hit an unexpected problem:\n\n{e.Exception.Message}",
            "MintyFilter",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
