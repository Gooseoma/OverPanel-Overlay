using System.Text.Json;
using Overpanel.Overlay.Services;
using Overpanel.Overlay.Services.Discord;

namespace Overpanel.Overlay;

internal static partial class SelfTest
{
    private static void CheckRoutes(OverlayConfig config, PanelStateTracker tracker)
    {
        Line(string.Empty);
        Line("2. Маршруты панели -> строка RPC");

        var cases = new (string Url, string Expected)[]
        {
            ("https://app.overpanel.ru/", config.Panel.DefaultDetails),
            ("https://app.overpanel.ru/players", "Игроки"),
            ("https://app.overpanel.ru/punishments/1034?tab=active", "Наказания"),
            ("https://app.overpanel.ru/anticheat", "Античит"),
            ("https://app.overpanel.ru/reports", "Репорты"),
            ("https://app.overpanel.ru/admins", "Администраторы"),
            ("https://app.overpanel.ru/chat", "Чат"),
            ("https://app.overpanel.ru/support", "оператор"),
        };

        foreach (var (url, expected) in cases)
        {
            tracker.ApplyNavigation(url, "title");
            var details = tracker.Current.Details;
            Check(
                $"{PanelStateTracker.NormalizePath(url)} -> {details}",
                details.Contains(expected, StringComparison.OrdinalIgnoreCase),
                $"ожидалось «{expected}»");
        }

        Line(string.Empty);
        Line("3. Действие «Выдает наказания» (data-overlay-action / web-сообщение)");
        tracker.ApplyNavigation("https://app.overpanel.ru/punishments", null);
        tracker.ApplyAction("punish");
        Check("действие перекрывает маршрут", tracker.Current.Details == "Выдает наказания", tracker.Current.Details);

        tracker.ApplyAction(null);
        Check(
            "после сброса действия снова маршрут",
            tracker.Current.Details.Contains("Наказания", StringComparison.Ordinal),
            tracker.Current.Details);
    }

    private static void CheckIdentity(OverlayConfig config, PanelStateTracker tracker)
    {
        Line(string.Empty);
        Line("4. Должность и ранг");

        tracker.ApplyIdentity(project: "NORMARUST", rank: 3);
        Check("ранг администратора", tracker.Current.RoleTag == "3", tracker.Current.RoleTag);
        Check("проект из панели", tracker.Current.Project == "NORMARUST", tracker.Current.Project);
        Check("должность по умолчанию", tracker.Current.RoleLabel == config.Identity.RoleLabel, tracker.Current.RoleLabel);

        var previousMaxRank = config.Identity.MaxRank;
        config.Identity.MaxRank = 11;

        tracker.ApplyIdentity(roleLabel: null, roleTag: null, rank: null, isOwner: true);
        Check("владелец: 11 + 1 = 12", tracker.Current.RoleTag == "12", tracker.Current.RoleTag);
        Check("владелец: подпись", tracker.Current.RoleLabel == config.Identity.OwnerLabel, tracker.Current.RoleLabel);

        config.Identity.MaxRank = previousMaxRank;
        tracker.ApplyIdentity(isOwner: false);
    }

    private static void CheckActivity(OverlayConfig config, PanelStateTracker tracker)
    {
        Line(string.Empty);
        Line("5. Шаблоны Discord activity");

        var activity = PresenceActivityFactory.Build(config, tracker.Current, DateTimeOffset.UtcNow, AppInfo.Version);

        Check("details = действие", activity.Details == tracker.Current.Details, activity.Details);
        Check(
            "state = «Должность [Ранг]»",
            activity.State == $"{tracker.Current.RoleLabel} [{tracker.Current.RoleTag}]",
            activity.State);
        Check(
            "кнопка OverPanel",
            activity.Buttons is { Count: 1 } && activity.Buttons[0].Url == "https://overpanel.ru");
        Check("details в пределах 2..128", activity.Details is { Length: >= 2 and <= 128 }, activity.Details);
        Check("state в пределах 2..128", activity.State is { Length: >= 2 and <= 128 }, activity.State);
        Check("тип активности из конфига", activity.Type == config.Presence.ActivityType, activity.Type.ToString());

        Line(string.Empty);
        Line("6. JSON для SET_ACTIVITY");
        Line(JsonSerializer.Serialize(activity, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task CheckIpcAsync(OverlayConfig config, bool live)
    {
        Line(string.Empty);
        Line("7. Discord IPC");

        if (!ConfigStore.HasClientId(config))
        {
            Line("  clientId не задан — RPC не заработает (см. README, «Discord Developer Portal»).");
        }

        var probeClientId = ConfigStore.HasClientId(config) ? config.Discord.ClientId.Trim() : "123456789012345678";

        var client = new DiscordIpcClient();
        client.Log += message => Line($"  [ipc] {message}");
        client.CommandError += (code, message) => Line($"  [ipc] ошибка {code}: {message}");

        try
        {
            var connected = await client.ConnectAsync(probeClientId, CancellationToken.None);
            Check("подключение к discord-ipc-*", connected);

            if (!connected || !live)
            {
                return;
            }

            if (!ConfigStore.HasClientId(config))
            {
                Line("  live-проверка пропущена: нужен валидный clientId в конфиге");
                return;
            }

            var activity = PresenceActivityFactory.Build(config, new PanelSnapshot
            {
                Path = "/selftest",
                Details = "Проверка оверлея",
                RoleLabel = config.Identity.RoleLabel,
                RoleTag = config.Identity.RoleTag,
                Project = string.IsNullOrWhiteSpace(config.Identity.Project) ? "OverPanel" : config.Identity.Project,
            }, DateTimeOffset.UtcNow, AppInfo.Version);

            Line("  выставляем тестовое присутствие на 5 секунд…");
            await client.SetActivityAsync(activity, CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(5));
            await client.SetActivityAsync(null, CancellationToken.None);
            Line("  присутствие снято");
        }
        catch (Exception ex)
        {
            Line($"  IPC недоступен: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}
