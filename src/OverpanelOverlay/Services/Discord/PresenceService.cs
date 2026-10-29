using System.Text.Json;

namespace Overpanel.Overlay.Services.Discord;

/// <summary>
/// Держит Discord Rich Presence в актуальном состоянии: подключение, реконнект,
/// троттлинг апдейтов (Discord: 5 обновлений за 20 секунд) и очистка при выходе.
/// </summary>
public sealed partial class PresenceService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions DedupeJson = new();

    private readonly OverlayConfig _config;
    private readonly PanelStateTracker _tracker;
    private readonly DiscordIpcClient _client = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly DateTimeOffset _sessionStart = DateTimeOffset.UtcNow;
    private readonly object _statusLock = new();

    private Task? _loop;
    private DateTimeOffset _lastSentAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextConnectAt = DateTimeOffset.MinValue;
    private string? _lastSentJson;
    private string _status = "Discord: ожидание";

    public PresenceService(OverlayConfig config, PanelStateTracker tracker)
    {
        _config = config;
        _tracker = tracker;

        _client.Log += message => AppLog.Debug($"[discord] {message}");
        _client.Ready += () =>
        {
            _lastSentJson = null;
            SetStatus("Discord: подключено");
        };
        _client.Disconnected += () =>
        {
            _lastSentJson = null;
            SetStatus("Discord: соединение потеряно");
        };
        _client.CommandError += (code, message) =>
        {
            // Ошибку вернул Discord: сбрасываем дедупликацию, иначе исправленное
            // состояние не отправится до следующего изменения. Троттлинг остаётся.
            _lastSentJson = null;

            SetStatus(string.IsNullOrWhiteSpace(message)
                ? $"Discord: ошибка {code}"
                : $"Discord: ошибка {code} — {message}");
        };
    }

    /// <summary>Короткий текст для статусной строки оверлея.</summary>
    public string Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public event Action<string>? StatusChanged;

    /// <summary>Активность, которая была бы отправлена прямо сейчас (для отладки/самопроверки).</summary>
    public DiscordActivity BuildCurrentActivity() =>
        PresenceActivityFactory.Build(_config, _tracker.Current, _sessionStart, AppInfo.Version);

    public void Start()
    {
        if (_loop != null)
        {
            return;
        }

        if (!_config.Presence.Enabled)
        {
            SetStatus("Discord RPC: выключен в конфиге");
            return;
        }

        if (!ConfigStore.HasClientId(_config))
        {
            SetStatus("Discord RPC: не задан clientId");
            return;
        }

        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Аккуратно снимает присутствие и закрывает IPC.</summary>
    public async Task ShutdownAsync()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_loop != null)
        {
            try
            {
                await Task.WhenAny(_loop, Task.Delay(1500)).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        if (_config.Presence.ClearOnExit)
        {
            try
            {
                if (_client.IsConnected)
                {
                    // Ограниченный таймаут: закрытие окна не должно ждать «залипший» пайп.
                    using var clearTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
                    await _client.SetActivityAsync(null, clearTimeout.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore
            }
        }

        await _client.DisconnectAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private void SetStatus(string status)
    {
        lock (_statusLock)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
        }

        AppLog.Info(status);
        StatusChanged?.Invoke(status);
    }
}
