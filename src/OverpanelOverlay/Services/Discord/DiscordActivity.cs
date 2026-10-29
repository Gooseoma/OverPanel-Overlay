using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Overpanel.Overlay.Services.Discord;

/// <summary>
/// activity-объект Discord (RPC SET_ACTIVITY).
/// Поля details/state — 2..128 символов, кнопок максимум 2.
/// </summary>
public sealed class DiscordActivity
{
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Details { get; set; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; set; }

    [JsonPropertyName("timestamps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordTimestamps? Timestamps { get; set; }

    [JsonPropertyName("assets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordAssets? Assets { get; set; }

    [JsonPropertyName("buttons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiscordButton>? Buttons { get; set; }

    [JsonPropertyName("instance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Instance { get; set; }
}

public sealed class DiscordTimestamps
{
    [JsonPropertyName("start")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Start { get; set; }

    [JsonPropertyName("end")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? End { get; set; }
}

public sealed class DiscordAssets
{
    [JsonPropertyName("large_image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LargeImage { get; set; }

    [JsonPropertyName("large_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LargeText { get; set; }

    [JsonPropertyName("small_image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SmallImage { get; set; }

    [JsonPropertyName("small_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SmallText { get; set; }
}

public sealed class DiscordButton
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}
