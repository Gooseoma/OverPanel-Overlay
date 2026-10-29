using System.Collections.Generic;

namespace Overpanel.Overlay.Services;

/// <summary>
/// Полный конфиг оверлея. Значения по умолчанию здесь и в overlay.config.json совпадают:
/// если файл отсутствует, он создаётся автоматически из этих значений.
/// </summary>
public sealed class OverlayConfig
{
    public WindowConfig Window { get; set; } = new();
    public HotkeysConfig Hotkeys { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public PanelConfig Panel { get; set; } = new();
    public IdentityConfig Identity { get; set; } = new();
    public PresenceConfig Presence { get; set; } = new();
    public DiscordConfig Discord { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public sealed class WindowConfig
{
    public double Width { get; set; } = 460;
    public double Height { get; set; } = 740;
    public double MinWidth { get; set; } = 360;
    public double MinHeight { get; set; } = 320;
    public bool Topmost { get; set; } = true;
    public bool RememberPosition { get; set; } = true;
}

public sealed class HotkeysConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Например "Ctrl+Shift+O".</summary>
    public string Toggle { get; set; } = "Ctrl+Shift+O";
}

public sealed class AuthConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Показывать экран входа, если сессии ещё нет.</summary>
    public bool RequireSignIn { get; set; } = true;

    /// <summary>Адрес панели, которая открывается в оверлее.</summary>
    public string PanelUrl { get; set; } = "https://app.overpanel.ru";

    /// <summary>SSO-страница панели (см. UPDATES.md п.15).</summary>
    public string SignInUrl { get; set; } = "https://api.overpanel.ru/overlay/sign-in";

    /// <summary>Схема callback-редиректа: overpanel-overlay://auth/callback?token=...</summary>
    public string CallbackScheme { get; set; } = "overpanel-overlay";

    public bool SendHwid { get; set; } = true;
    public string HwidParam { get; set; } = "hwid";

    /// <summary>Доп. query-параметры к signInUrl (например app=overlay).</summary>
    public Dictionary<string, string> ExtraParams { get; set; } = new() { ["app"] = "overlay" };

    /// <summary>Имена query-параметров, из которых забирается токен в callback.</summary>
    public List<string> TokenParamNames { get; set; } = new() { "token", "access_token", "code" };

    /// <summary>Если после входа мы оказались на одном из этих префиксов — вход считаем успешным.</summary>
    public List<string> SuccessUrlPrefixes { get; set; } = new();

    /// <summary>
    /// Опционально: положить токен в localStorage панели, если она его там ждёт.
    /// Пример: [{ "key": "accessToken", "template": "{token}" }]
    /// </summary>
    public List<StorageInjection> StorageInjections { get; set; } = new();
}

public sealed class StorageInjection
{
    public string Key { get; set; } = string.Empty;
    public string Template { get; set; } = "{token}";
}

public sealed class PanelConfig
{
    public string DefaultDetails { get; set; } = "На главной странице";

    /// <summary>Path-prefix -> строка RPC. Побеждает самое длинное совпадение.</summary>
    public List<RouteRule> Routes { get; set; } = new();

    /// <summary>actionId -> строка RPC (для data-overlay-action / web-сообщений).</summary>
    public Dictionary<string, string> Actions { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Сколько секунд держится действие (напр. "Выдает наказания").</summary>
    public int ActionTtlSeconds { get; set; } = 150;

    public InjectedScriptConfig Injected { get; set; } = new();

    /// <summary>Добавка к User-Agent, чтобы бэкенд мог отличить оверлей от браузера.</summary>
    public string UserAgentSuffix { get; set; } = "OverpanelOverlay/1.0";
}

public sealed class RouteRule
{
    public string Match { get; set; } = "/";
    public string Details { get; set; } = string.Empty;
}

public sealed class InjectedScriptConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Ловить атрибут data-overlay-action (кнопки «Заблокировать» и т.п.).</summary>
    public bool DetectActionAttributes { get; set; } = true;

    public string AttributeName { get; set; } = "data-overlay-action";

    public SelectorsConfig Selectors { get; set; } = new();

    public int ScanIntervalMs { get; set; } = 1000;
}

public sealed class SelectorsConfig
{
    /// <summary>CSS-селекторы контекста. Пустое значение = не собирать.</summary>
    public string Project { get; set; } = "[data-overlay-project]";

    public string RoleLabel { get; set; } = "[data-overlay-role-label]";
    public string RoleTag { get; set; } = "[data-overlay-role-tag]";
    public string DisplayName { get; set; } = "[data-overlay-user]";
    public string Rank { get; set; } = "[data-overlay-rank]";
}
