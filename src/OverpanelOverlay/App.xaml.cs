using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Overpanel.Overlay.Services;
using Overpanel.Overlay.Services.Discord;

namespace Overpanel.Overlay;

public partial class App : Application
{
    private const string MutexName = @"Local\Overpanel.Overlay.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private PanelStateTracker? _tracker;
    private PresenceService? _presence;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;

        if (args.Any(arg => arg.Equals("--selftest", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("--selftest-live", StringComparison.OrdinalIgnoreCase)))
        {
            var live = args.Any(arg => arg.Equals("--selftest-live", StringComparison.OrdinalIgnoreCase));

            // Не блокируем UI-поток: внутри самопроверки есть await к Discord IPC,
            // и синхронное ожидание на диспетчере приводило к дедлоку.
            _ = RunSelfTestAsync(args, live);
            return;
        }

        _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Оверлей OverPanel уже запущен.",
                "OverPanel Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        var config = ConfigStore.Load(GetArgumentValue(args, "--config"), out var configPath, out var warnings);
        AppLog.Configure(config.Logging.Enabled, config.Logging.Level);

        foreach (var warning in warnings)
        {
            AppLog.Warn(warning);
        }

        AppLog.Info($"OverPanel Overlay {AppInfo.Version} запускается. Конфиг: {configPath}");
        AppLog.Info($"HWID устройства: {DeviceIdentity.Hwid}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _tracker = new PanelStateTracker(config);
        _presence = new PresenceService(config, _tracker);

        var window = new MainWindow(config, _tracker, _presence);
        MainWindow = window;
        window.Show();

        _presence.Start();
    }

    private async Task RunSelfTestAsync(string[] args, bool live)
    {
        var exitCode = 2;
        try
        {
            exitCode = await SelfTest.RunAsync(args, live);
        }
        catch (Exception ex)
        {
            AppLog.Exception("Самопроверка завершилась ошибкой", ex);
        }

        Shutdown(exitCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _tracker?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // ignore
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Exception("Необработанное исключение UI", e.Exception);

        MessageBox.Show(
            $"Произошла ошибка: {e.Exception.Message}\n\nПодробности в логе:\n{AppPaths.LogFile}",
            "OverPanel Overlay",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        var inline = args.FirstOrDefault(arg =>
            arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));

        return inline?[(name.Length + 1)..];
    }
}
