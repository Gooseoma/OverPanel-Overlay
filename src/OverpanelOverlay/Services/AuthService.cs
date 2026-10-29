using System.Collections.Generic;
using System.Text;

namespace Overpanel.Overlay.Services;

/// <summary>
/// SSO-вход (UPDATES.md п.15): уводим пользователя на api.overpanel.ru/overlay/sign-in,
/// передаём HWID, ждём редирект обратно на overpanel-overlay://auth/callback или на панель.
/// </summary>
public sealed class AuthService
{
    private readonly OverlayConfig _config;

    public AuthService(OverlayConfig config)
    {
        _config = config;
    }

    public string CallbackUrl => $"{_config.Auth.CallbackScheme}://auth/callback";

    /// <summary>URL страницы входа с HWID, версией и redirect.</summary>
    public string BuildSignInUrl()
    {
        var baseUrl = _config.Auth.SignInUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return _config.Auth.PanelUrl;
        }

        var sb = new StringBuilder(baseUrl);
        var hasQuery = baseUrl.Contains('?');

        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            sb.Append(hasQuery ? '&' : '?');
            hasQuery = true;
            sb.Append(Uri.EscapeDataString(key.Trim()));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value.Trim()));
        }

        foreach (var pair in _config.Auth.ExtraParams)
        {
            Add(pair.Key, pair.Value);
        }

        Add("redirect", CallbackUrl);
        Add("v", AppInfo.Version);

        if (_config.Auth.SendHwid)
        {
            Add(_config.Auth.HwidParam, DeviceIdentity.Hwid);
        }

        return sb.ToString();
    }

    /// <summary>Является ли URL callback-редиректом оверлея (по схеме).</summary>
    public bool IsCallbackUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, _config.Auth.CallbackScheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Достаёт токен из query или fragment callback-ссылки.</summary>
    public string? TryGetToken(Uri uri)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ReadPairs(uri.Query, parsed);
        ReadPairs(uri.Fragment, parsed);

        foreach (var name in _config.Auth.TokenParamNames)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                parsed.TryGetValue(name.Trim(), out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Считается ли URL признаком успешного входа (обычно — адрес панели).</summary>
    public bool IsSuccessUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (IsCallbackUrl(url))
        {
            return true;
        }

        foreach (var prefix in _config.Auth.SuccessUrlPrefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix) &&
                url.StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>JavaScript для записи токена в localStorage панели (если настроено).</summary>
    public IEnumerable<string> BuildStorageInjectionScripts(string token)
    {
        foreach (var injection in _config.Auth.StorageInjections)
        {
            if (string.IsNullOrWhiteSpace(injection.Key))
            {
                continue;
            }

            var value = (injection.Template ?? "{token}").Replace("{token}", token, StringComparison.Ordinal);
            var keyJson = System.Text.Json.JsonSerializer.Serialize(injection.Key.Trim());
            var valueJson = System.Text.Json.JsonSerializer.Serialize(value);

            yield return $"try {{ window.localStorage.setItem({keyJson}, {valueJson}); }} catch (e) {{ }}";
        }
    }

    private static void ReadPairs(string? raw, Dictionary<string, string> target)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var value = raw.TrimStart('?', '#');
        if (value.Length == 0)
        {
            return;
        }

        foreach (var pair in value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separator]);
            var rawValue = pair[(separator + 1)..];
            target[key] = Uri.UnescapeDataString(rawValue.Replace("+", " "));
        }
    }
}
