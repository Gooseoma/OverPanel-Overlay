using System.IO;
using System.Reflection;

namespace Overpanel.Overlay.Services;

/// <summary>Пути, которые использует оверлей (ничего не пишем рядом с чужими файлами).</summary>
public static class AppPaths
{
    public const string AppFolderName = "Overpanel";
    public const string AppName = "Overlay";

    /// <summary>Папка, из которой запущен exe (там же лежит overlay.config.json).</summary>
    public static string BaseDirectory => AppContext.BaseDirectory;

    /// <summary>Путь к конфигу по умолчанию.</summary>
    public static string DefaultConfigPath => Path.Combine(BaseDirectory, "overlay.config.json");

    /// <summary>%LOCALAPPDATA%\Overpanel\Overlay</summary>
    public static string UserDataDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(root, AppFolderName, AppName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Отдельный persistent-профиль WebView2: куки живут между запусками.</summary>
    public static string WebViewDataDirectory
    {
        get
        {
            var dir = Path.Combine(UserDataDirectory, "WebView2");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Состояние окна/сессии.</summary>
    public static string StateFile => Path.Combine(UserDataDirectory, "state.json");

    /// <summary>Лог-файл.</summary>
    public static string LogFile => Path.Combine(UserDataDirectory, "logs", "overlay.log");

    /// <summary>Отчёт самопроверки (--selftest).</summary>
    public static string SelfTestReportFile => Path.Combine(UserDataDirectory, "selftest-report.txt");
}

/// <summary>Информация о сборке.</summary>
public static class AppInfo
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static string UserAgentSuffix => $"OverpanelOverlay/{Version}";
}
