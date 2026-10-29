using System.Collections.Generic;

namespace Overpanel.Overlay.Services.Discord;

/// <summary>Собирает Discord activity строго по шаблонам из конфига (UPDATES.md п.15).</summary>
public static class PresenceActivityFactory
{
    public static DiscordActivity Build(
        OverlayConfig config,
        PanelSnapshot snapshot,
        DateTimeOffset sessionStart,
        string version)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["action"] = snapshot.Details,
            ["details"] = snapshot.Details,
            ["roleLabel"] = snapshot.RoleLabel,
            ["roleTag"] = snapshot.RoleTag,
            ["role"] = TemplateRenderer.Render("{roleLabel} [{roleTag}]", new Dictionary<string, string?>
            {
                ["roleLabel"] = snapshot.RoleLabel,
                ["roleTag"] = snapshot.RoleTag,
            }),
            ["project"] = snapshot.Project,
            ["displayName"] = snapshot.DisplayName,
            ["path"] = snapshot.Path,
            ["title"] = snapshot.Title,
            ["version"] = version,
            ["app"] = "OverPanel",
        };

        var activity = new DiscordActivity
        {
            Type = config.Presence.ActivityType,
            Details = TemplateRenderer.ToPresenceField(TemplateRenderer.Render(config.Presence.DetailsTemplate, values)),
            State = TemplateRenderer.ToPresenceField(TemplateRenderer.Render(config.Presence.StateTemplate, values)),
        };

        if (config.Presence.ShowElapsedTime)
        {
            activity.Timestamps = new DiscordTimestamps { Start = sessionStart.ToUnixTimeSeconds() };
        }

        var assets = new DiscordAssets();

        if (!string.IsNullOrWhiteSpace(config.Presence.LargeImageKey))
        {
            assets.LargeImage = config.Presence.LargeImageKey.Trim();
            assets.LargeText = TemplateRenderer.ToPresenceField(
                TemplateRenderer.Render(config.Presence.LargeTextTemplate, values));
        }

        if (!string.IsNullOrWhiteSpace(config.Presence.SmallImageKey))
        {
            assets.SmallImage = config.Presence.SmallImageKey.Trim();
            assets.SmallText = TemplateRenderer.ToPresenceField(
                TemplateRenderer.Render(config.Presence.SmallTextTemplate, values));
        }

        if (assets.LargeImage != null || assets.SmallImage != null)
        {
            activity.Assets = assets;
        }

        var buttons = new List<DiscordButton>();
        foreach (var button in config.Presence.Buttons)
        {
            if (buttons.Count >= 2)
            {
                break;
            }

            var label = TemplateRenderer.ToButtonLabel(button.Label);
            if (label == null || !IsHttpUrl(button.Url))
            {
                continue;
            }

            buttons.Add(new DiscordButton { Label = label, Url = button.Url.Trim() });
        }

        if (buttons.Count > 0)
        {
            activity.Buttons = buttons;
        }

        return activity;
    }

    private static bool IsHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
