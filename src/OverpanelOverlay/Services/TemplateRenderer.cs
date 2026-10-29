using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Overpanel.Overlay.Services;

/// <summary>Подстановка {key} в шаблоны RPC и чистка результата под требования Discord.</summary>
public static partial class TemplateRenderer
{
    public const int MaxFieldLength = 128;
    public const int MinFieldLength = 2;

    [GeneratedRegex(@"\{([a-zA-Z0-9_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\[\s*\]|\(\s*\)|\{\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex EmptyBracketsRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"^[\s\-–—·|,:;]+|[\s\-–—·|,:;]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EdgeSeparatorsRegex();

    /// <summary>Подставляет значения, убирает пустые скобки и висящие разделители.</summary>
    public static string Render(string? template, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var result = PlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        });

        result = EmptyBracketsRegex().Replace(result, string.Empty);
        result = result.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        result = MultiSpaceRegex().Replace(result, " ");
        result = EdgeSeparatorsRegex().Replace(result, string.Empty);

        return result.Trim();
    }

    /// <summary>
    /// Приводит строку к виду, который Discord реально отобразит:
    /// 2..128 символов, без переводов строк. null = поле не отправлять.
    /// </summary>
    public static string? ToPresenceField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        text = MultiSpaceRegex().Replace(text, " ");

        if (text.Length > MaxFieldLength)
        {
            text = text[..(MaxFieldLength - 1)].TrimEnd() + "…";
        }

        return text.Length >= MinFieldLength ? text : null;
    }

    /// <summary>Строка для приглашения в кнопке.</summary>
    public static string? ToButtonLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var text = label.Trim();
        return text.Length <= 32 ? text : text[..32];
    }
}
