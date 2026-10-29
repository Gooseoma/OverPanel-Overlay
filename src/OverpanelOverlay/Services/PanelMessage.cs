using System.Text.Json;

namespace Overpanel.Overlay.Services;

/// <summary>Сообщение из страницы панели (WebView2 WebMessageReceived).</summary>
public sealed class PanelMessage
{
    public string Kind { get; init; } = string.Empty;
    public string? Path { get; init; }
    public string? Href { get; init; }
    public string? Title { get; init; }
    public string? Action { get; init; }
    public string? Project { get; init; }
    public string? RoleLabel { get; init; }
    public string? RoleTag { get; init; }
    public string? DisplayName { get; init; }
    public int? Rank { get; init; }
    public bool? IsOwner { get; init; }
    public string? Token { get; init; }

    public static PanelMessage? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json[0] != '{')
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Чужие сообщения не трогаем: принимаем свои и «безымянные».
            if (root.TryGetProperty("source", out var source) &&
                source.ValueKind == JsonValueKind.String &&
                !string.Equals(source.GetString(), PanelBridge.SourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var kind = GetString(root, "kind");
            if (string.IsNullOrWhiteSpace(kind))
            {
                return null;
            }

            return new PanelMessage
            {
                Kind = kind.Trim().ToLowerInvariant(),
                Path = GetString(root, "path"),
                Href = GetString(root, "href"),
                Title = GetString(root, "title"),
                Action = GetString(root, "action"),
                Project = GetString(root, "project"),
                RoleLabel = GetString(root, "roleLabel"),
                RoleTag = GetString(root, "roleTag"),
                DisplayName = GetString(root, "displayName"),
                Rank = GetInt(root, "rank"),
                IsOwner = GetBool(root, "isOwner"),
                Token = GetString(root, "token"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            _ => null,
        };
    }

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
