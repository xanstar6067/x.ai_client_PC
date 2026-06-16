using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using x.ai_client_PC.Data;
using x.ai_client_PC.Infrastructure;
using x.ai_client_PC.Services;
using x.ai_client_PC.Services.Api;
using x.ai_client_PC.ViewModels;

namespace x.ai_client_PC;

public partial class App : Application
{
    private readonly ServiceProvider _services;

    public App()
    {
        AppPaths.EnsureDirectories();

        var services = new ServiceCollection();
        services.AddSingleton(_ => new AppDbContext(AppPaths.DatabasePath));
        services.AddSingleton<HttpClient>();
        services.AddSingleton<XaiApiClient>();
        services.AddSingleton<DataRepository>();
        services.AddSingleton<ModelCatalogService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ChatGenerationService>();
        services.AddSingleton<ImageGenerationService>();
        services.AddSingleton<VideoGenerationService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SetupExceptionLogging();
        try
        {
            var mainWindow = _services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            LogException("OnStartup", ex);
            MessageBox.Show(ex.ToString(), "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void SetupExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogException("UnhandledException", ex);
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            LogException("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(args.Exception.ToString(), "Runtime error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void LogException(string source, Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppPaths.AppDataRoot, "error.log");
            var text = $"[{DateTime.UtcNow:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
            File.AppendAllText(logPath, text);
        }
        catch
        {
            // Best effort only.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services.Dispose();
        base.OnExit(e);
    }
}
