using System.Collections.Generic;

namespace Overpanel.Overlay.Services;

public sealed class IdentityConfig
{
    /// <summary>Название проекта для RPC, если панель его не отдала (напр. NORMARUST).</summary>
    public string Project { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string RoleLabel { get; set; } = "Оператор панели";
    public string RoleTag { get; set; } = "Оператор";

    public string OwnerLabel { get; set; } = "Владелец проекта";

    /// <summary>Метка для оператора (в RPC пишется [Оператор]).</summary>
    public string OperatorTag { get; set; } = "Оператор";

    /// <summary>Всего рангов на проекте (0 = неизвестно).</summary>
    public int MaxRank { get; set; }

    /// <summary>Ранг владельца = maxRank + ownerRankBonus.</summary>
    public int OwnerRankBonus { get; set; } = 1;
}

public sealed class PresenceConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>0=Playing, 2=Listening, 3=Watching, 5=Competing.</summary>
    public int ActivityType { get; set; } = 3;

    public string DetailsTemplate { get; set; } = "{action}";
    public string StateTemplate { get; set; } = "{roleLabel} [{roleTag}]";

    public string LargeImageKey { get; set; } = string.Empty;
    public string LargeTextTemplate { get; set; } = "OverPanel | {project}";
    public string SmallImageKey { get; set; } = string.Empty;
    public string SmallTextTemplate { get; set; } = string.Empty;

    public List<PresenceButton> Buttons { get; set; } = new() { new PresenceButton() };

    public bool ShowElapsedTime { get; set; } = true;

    /// <summary>Discord лимитирует 5 апдейтов за 20 секунд.</summary>
    public int MinUpdateIntervalSeconds { get; set; } = 15;

    public bool ClearOnExit { get; set; } = true;
}

public sealed class PresenceButton
{
    public string Label { get; set; } = "OverPanel";
    public string Url { get; set; } = "https://overpanel.ru";
}

public sealed class DiscordConfig
{
    /// <summary>Application ID из Discord Developer Portal. Пусто = RPC выключен.</summary>
    public string ClientId { get; set; } = string.Empty;

    public int ReconnectSeconds { get; set; } = 15;
}

public sealed class LoggingConfig
{
    public bool Enabled { get; set; } = true;
    public string Level { get; set; } = "info";
}
