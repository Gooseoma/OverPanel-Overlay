using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Overpanel.Overlay.Services;

namespace Overpanel.Overlay;

/// <summary>
/// --selftest / --selftest-live: проверка логики без открытия окна.
/// Отчёт пишется в %LOCALAPPDATA%\Overpanel\Overlay\selftest-report.txt.
/// </summary>
internal static partial class SelfTest
{
    private static readonly StringBuilder Report = new();
    private static int _failures;

    public static async Task<int> RunAsync(string[] args, bool live)
    {
        var config = ConfigStore.Load(GetArgumentValue(args, "--config"), out var configPath, out var warnings);
        AppLog.Configure(config.Logging.Enabled, config.Logging.Level);

        Line($"OverPanel Overlay {AppInfo.Version} — самопроверка ({(live ? "live" : "dry")})");
        Line($"Конфиг: {configPath}");

        foreach (var warning in warnings)
        {
            Line($"  предупреждение: {warning}");
        }

        using var tracker = new PanelStateTracker(config);

        Line(string.Empty);
        Line("1. Идентификация устройства");
        var hwid = DeviceIdentity.Hwid;
        Line($"  HWID: {hwid}");
        Check("HWID не пустой", hwid.Length > 20, hwid);

        CheckRoutes(config, tracker);
        CheckIdentity(config, tracker);
        CheckActivity(config, tracker);
        await CheckIpcAsync(config, live);

        Line(string.Empty);
        Line(_failures == 0 ? "ИТОГ: все проверки пройдены" : $"ИТОГ: провалено проверок — {_failures}");

        WriteReport();

        return _failures == 0 ? 0 : 1;
    }

    private static void Line(string text) => Report.AppendLine(text);

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok)
        {
            Line($"  OK    {name}");
            return;
        }

        _failures++;
        Line($"  FAIL  {name}{(string.IsNullOrEmpty(detail) ? string.Empty : $": {detail}")}");
    }

    private static void WriteReport()
    {
        try
        {
            var path = AppPaths.SelfTestReportFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Report.ToString(), Encoding.UTF8);
        }
        catch
        {
            // отчёт — вспомогательный
        }

        AttachConsoleToParent();
        Console.Write(Report.ToString());
        Console.WriteLine($"Отчёт: {AppPaths.SelfTestReportFile}");
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private static void AttachConsoleToParent()
    {
        try
        {
            if (AttachConsole(-1))
            {
                var stdout = Console.OpenStandardOutput();
                Console.SetOut(new StreamWriter(stdout) { AutoFlush = true });
            }
        }
        catch
        {
            // консоль необязательна: отчёт уже записан в файл
        }
    }
}
