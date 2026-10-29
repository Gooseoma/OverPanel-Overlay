using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Overpanel.Overlay.Services;

/// <summary>Чтение/запись overlay.config.json.</summary>
public static class ConfigStore
{
    public const string EnvClientId = "OVERPANEL_DISCORD_CLIENT_ID";
    public const string EnvConfigPath = "OVERPANEL_OVERLAY_CONFIG";

    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static OverlayConfig Load(string? explicitPath, out string usedPath, out List<string> warnings)
    {
        warnings = new List<string>();

        usedPath = explicitPath
                   ?? Environment.GetEnvironmentVariable(EnvConfigPath)
                   ?? AppPaths.DefaultConfigPath;

        OverlayConfig config;

        if (!File.Exists(usedPath))
        {
            config = new OverlayConfig();
            try
            {
                var dir = Path.GetDirectoryName(usedPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(usedPath, JsonSerializer.Serialize(config, WriteOptions));
                warnings.Add($"Конфиг не найден, создан по умолчанию: {usedPath}");
            }
            catch (Exception ex)
            {
                warnings.Add($"Конфиг не найден и создать не удалось ({ex.Message}), используются значения по умолчанию");
            }

            Normalize(config);
            return config;
        }

        try
        {
            var json = File.ReadAllText(usedPath);
            config = JsonSerializer.Deserialize<OverlayConfig>(json, ReadOptions) ?? new OverlayConfig();
        }
        catch (Exception ex)
        {
            warnings.Add($"Не удалось прочитать {usedPath} ({ex.Message}), используются значения по умолчанию");
            config = new OverlayConfig();
        }

        var envClientId = Environment.GetEnvironmentVariable(EnvClientId);
        if (!string.IsNullOrWhiteSpace(envClientId))
        {
            config.Discord.ClientId = envClientId.Trim();
            warnings.Add($"clientId переопределён переменной окружения {EnvClientId}");
        }

        Normalize(config);
        return config;
    }

    public static void Normalize(OverlayConfig config)
    {
        config.Window.MinWidth = Math.Max(240, config.Window.MinWidth);
        config.Window.MinHeight = Math.Max(200, config.Window.MinHeight);
        config.Window.Width = Math.Max(config.Window.MinWidth, config.Window.Width);
        config.Window.Height = Math.Max(config.Window.MinHeight, config.Window.Height);

        config.Panel.ActionTtlSeconds = Math.Clamp(config.Panel.ActionTtlSeconds, 5, 3600);
        config.Panel.Injected.ScanIntervalMs = Math.Clamp(config.Panel.Injected.ScanIntervalMs, 250, 10000);
        config.Presence.MinUpdateIntervalSeconds = Math.Clamp(config.Presence.MinUpdateIntervalSeconds, 15, 300);
        config.Presence.ActivityType = Math.Clamp(config.Presence.ActivityType, 0, 5);
        config.Discord.ReconnectSeconds = Math.Clamp(config.Discord.ReconnectSeconds, 5, 300);

        if (string.IsNullOrWhiteSpace(config.Auth.PanelUrl))
        {
            config.Auth.PanelUrl = "https://app.overpanel.ru";
        }

        if (config.Auth.SuccessUrlPrefixes.Count == 0)
        {
            config.Auth.SuccessUrlPrefixes.Add(config.Auth.PanelUrl);
        }

        config.Panel.Routes ??= new List<RouteRule>();
        config.Panel.Actions ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        config.Presence.Buttons ??= new List<PresenceButton>();
        config.Auth.StorageInjections ??= new List<StorageInjection>();
        config.Auth.ExtraParams ??= new Dictionary<string, string>();
        config.Auth.TokenParamNames ??= new List<string>();
        config.Panel.Injected.Selectors ??= new SelectorsConfig();
    }

    /// <summary>Валиден ли clientId (не пустой, только цифры, 17-21 символ).</summary>
    public static bool HasClientId(OverlayConfig config)
    {
        var id = config.Discord.ClientId?.Trim() ?? string.Empty;
        if (id.Length < 17 || id.Length > 21)
        {
            return false;
        }

        foreach (var ch in id)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
