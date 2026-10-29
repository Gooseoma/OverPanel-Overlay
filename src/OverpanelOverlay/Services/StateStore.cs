using System.IO;
using System.Text.Json;

namespace Overpanel.Overlay.Services;

/// <summary>Состояние, которое оверлей помнит между запусками (не конфиг).</summary>
public sealed class OverlayState
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool? Topmost { get; set; }

    /// <summary>Пользователь уже проходил SSO (куки WebView2 при этом живут в профиле).</summary>
    public bool SignedIn { get; set; }

    public string? LastAuthAt { get; set; }
}

public static class StateStore
{
    private static OverlayState? _cached;
    private static readonly object Sync = new();

    public static OverlayState Load()
    {
        lock (Sync)
        {
            if (_cached != null)
            {
                return _cached;
            }

            try
            {
                if (File.Exists(AppPaths.StateFile))
                {
                    var json = File.ReadAllText(AppPaths.StateFile);
                    _cached = JsonSerializer.Deserialize<OverlayState>(json, ConfigStore.ReadOptions) ?? new OverlayState();
                    return _cached;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Не удалось прочитать state.json: {ex.Message}");
            }

            _cached = new OverlayState();
            return _cached;
        }
    }

    public static void Save(OverlayState state)
    {
        lock (Sync)
        {
            _cached = state;
            try
            {
                File.WriteAllText(AppPaths.StateFile, JsonSerializer.Serialize(state, ConfigStore.WriteOptions));
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Не удалось сохранить state.json: {ex.Message}");
            }
        }
    }
}
