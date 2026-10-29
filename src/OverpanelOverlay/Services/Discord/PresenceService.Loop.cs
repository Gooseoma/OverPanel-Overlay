using System.Text.Json;

namespace Overpanel.Overlay.Services.Discord;

public sealed partial class PresenceService
{
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var clientId = _config.Discord.ClientId.Trim();
        var minInterval = TimeSpan.FromSeconds(_config.Presence.MinUpdateIntervalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected && DateTimeOffset.UtcNow >= _nextConnectAt)
                {
                    _nextConnectAt = DateTimeOffset.UtcNow.AddSeconds(_config.Discord.ReconnectSeconds);
                    SetStatus("Discord: подключаемся…");

                    if (await _client.ConnectAsync(clientId, cancellationToken).ConfigureAwait(false))
                    {
                        _lastSentAt = DateTimeOffset.MinValue;
                    }
                    else
                    {
                        SetStatus("Discord не запущен — ждём");
                    }
                }

                if (_client.IsConnected && DateTimeOffset.UtcNow - _lastSentAt >= minInterval)
                {
                    var activity = BuildCurrentActivity();
                    var json = JsonSerializer.Serialize(activity, DedupeJson);

                    if (!string.Equals(json, _lastSentJson, StringComparison.Ordinal))
                    {
                        await _client.SetActivityAsync(activity, cancellationToken).ConfigureAwait(false);
                        _lastSentJson = json;
                        _lastSentAt = DateTimeOffset.UtcNow;
                        AppLog.Debug($"[discord] SET_ACTIVITY: {activity.Details} / {activity.State}");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Presence loop: {ex.GetType().Name}: {ex.Message}");
                _lastSentJson = null;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
