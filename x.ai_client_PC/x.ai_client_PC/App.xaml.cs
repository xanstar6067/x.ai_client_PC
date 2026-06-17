using System.IO;
using System.Windows;
using System.Windows.Threading;
using x.ai_client_PC.Services;

namespace x.ai_client_PC;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                WriteCrashLog(exception);
            }
        };

        try
        {
            var mainWindow = new MainWindow();

            if (e.Args.Any(arg => string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
            {
                mainWindow.Close();
                Shutdown(0);
                return;
            }

            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
            Shutdown(-1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        MessageBox.Show(
            "Приложение поймало исключение и записало его в crash.log.\n\n" + e.Exception.Message,
            "xAI Grok Chat PC",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppStorage.AppDataPath);
            File.AppendAllText(
                Path.Combine(AppStorage.AppDataPath, "crash.log"),
                $"[{DateTimeOffset.Now:O}]\n{exception}\n\n");
        }
        catch
        {
            // If logging fails there is nothing useful left to do here.
        }
    }
}
