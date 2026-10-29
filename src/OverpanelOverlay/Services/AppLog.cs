using System.Diagnostics;
using System.IO;
using System.Text;

namespace Overpanel.Overlay.Services;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
}

/// <summary>Простой потокобезопасный файловый лог + событие для статусной строки UI.</summary>
public static class AppLog
{
    private static readonly object Sync = new();
    private static LogLevel _minLevel = LogLevel.Info;
    private static bool _toFile = true;
    private const long MaxFileSize = 1024 * 1024;

    /// <summary>Событие для UI (приходит с любого потока).</summary>
    public static event Action<string>? Message;

    public static void Configure(bool toFile, string level)
    {
        _toFile = toFile;
        _minLevel = Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) ? parsed : LogLevel.Info;
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Exception(string message, Exception ex) =>
        Write(LogLevel.Error, $"{message}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(LogLevel level, string message)
    {
        if (level < _minLevel)
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-5}] {message}";
        System.Diagnostics.Debug.WriteLine(line);

        if (_toFile)
        {
            try
            {
                lock (Sync)
                {
                    var path = AppPaths.LogFile;
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    RollIfNeeded(path);
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // лог не должен ронять приложение
            }
        }

        try
        {
            Message?.Invoke(line);
        }
        catch
        {
            // подписчик не должен ронять приложение
        }
    }

    private static void RollIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxFileSize)
        {
            return;
        }

        var backup = path + ".1";
        File.Delete(backup);
        File.Move(path, backup);
    }
}
