namespace Overpanel.Overlay.Services;

/// <summary>
/// Склеивает сигналы от WebView2 (URL, injected-скрипт, web-сообщения) в один снимок состояния.
/// </summary>
public sealed class PanelStateTracker : IDisposable
{
    private readonly OverlayConfig _config;
    private readonly object _sync = new();
    private readonly Timer _expiryTimer;

    private string _path = "/";
    private string _title = string.Empty;
    private string? _actionId;
    private DateTimeOffset _actionExpiresAt = DateTimeOffset.MinValue;

    private string? _panelProject;
    private string? _panelRoleLabel;
    private string? _panelRoleTag;
    private string? _panelDisplayName;
    private int? _panelRank;
    private bool _isOwner;

    private PanelSnapshot _current;

    public PanelStateTracker(OverlayConfig config)
    {
        _config = config;
        _expiryTimer = new Timer(OnExpiry, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _current = Build();
    }

    /// <summary>Вызывается с любого потока.</summary>
    public event EventHandler? Changed;

    public PanelSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    /// <summary>Новая навигация внутри WebView2.</summary>
    public void ApplyNavigation(string? url, string? title)
    {
        lock (_sync)
        {
            _path = NormalizePath(url);
            _title = title ?? string.Empty;
        }

        Rebuild();
    }

    /// <summary>Действие из data-overlay-action или web-сообщения. null = сбросить.</summary>
    public void ApplyAction(string? actionId)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                _actionId = null;
                _actionExpiresAt = DateTimeOffset.MinValue;
            }
            else
            {
                _actionId = actionId.Trim();
                _actionExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_config.Panel.ActionTtlSeconds);
            }
        }

        RescheduleExpiry();
        Rebuild();
    }

    /// <summary>Данные об администраторе/проекте (из DOM-селекторов или web-сообщения).</summary>
    public void ApplyIdentity(
        string? project = null,
        string? roleLabel = null,
        string? roleTag = null,
        int? rank = null,
        bool? isOwner = null,
        string? displayName = null)
    {
        lock (_sync)
        {
            if (project != null)
            {
                _panelProject = Blank(project);
            }

            if (roleLabel != null)
            {
                _panelRoleLabel = Blank(roleLabel);
            }

            if (roleTag != null)
            {
                _panelRoleTag = Blank(roleTag);
            }

            if (displayName != null)
            {
                _panelDisplayName = Blank(displayName);
            }

            if (rank.HasValue)
            {
                _panelRank = rank.Value > 0 ? rank : null;
            }

            if (isOwner.HasValue)
            {
                _isOwner = isOwner.Value;
            }
        }

        Rebuild();
    }

    public void Dispose() => _expiryTimer.Dispose();

    private void OnExpiry(object? state)
    {
        var expired = false;
        lock (_sync)
        {
            if (_actionId != null && DateTimeOffset.UtcNow >= _actionExpiresAt)
            {
                _actionId = null;
                _actionExpiresAt = DateTimeOffset.MinValue;
                expired = true;
            }
        }

        if (expired)
        {
            Rebuild();
        }
    }

    private void RescheduleExpiry()
    {
        TimeSpan due;
        lock (_sync)
        {
            due = _actionId == null ? Timeout.InfiniteTimeSpan : _actionExpiresAt - DateTimeOffset.UtcNow;
        }

        if (due == Timeout.InfiniteTimeSpan || due <= TimeSpan.Zero)
        {
            _expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        _expiryTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void Rebuild()
    {
        var next = Build();
        bool changed;
        lock (_sync)
        {
            changed = !next.SameAs(_current);
            _current = next;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private PanelSnapshot Build()
    {
        string path;
        string title;
        string? actionId;
        string? project;
        string? roleLabel;
        string? roleTag;
        string? displayName;
        int? rank;
        bool isOwner;

        lock (_sync)
        {
            path = _path;
            title = _title;
            actionId = _actionId;
            project = _panelProject;
            roleLabel = _panelRoleLabel;
            roleTag = _panelRoleTag;
            displayName = _panelDisplayName;
            rank = _panelRank;
            isOwner = _isOwner;
        }

        return new PanelSnapshot
        {
            Path = path,
            Title = title,
            Details = ResolveDetails(path, actionId),
            RoleLabel = ResolveRoleLabel(roleLabel, isOwner),
            RoleTag = ResolveRoleTag(roleTag, rank, isOwner),
            Project = FirstNonBlank(project, _config.Identity.Project),
            DisplayName = FirstNonBlank(displayName, _config.Identity.DisplayName),
            IsAction = actionId != null,
        };
    }

    private string ResolveDetails(string path, string? actionId)
    {
        if (!string.IsNullOrWhiteSpace(actionId) &&
            _config.Panel.Actions.TryGetValue(actionId, out var actionText) &&
            !string.IsNullOrWhiteSpace(actionText))
        {
            return actionText;
        }

        RouteRule? best = null;
        var bestLength = -1;

        foreach (var rule in _config.Panel.Routes)
        {
            var match = NormalizePath(rule.Match);
            if (string.IsNullOrWhiteSpace(match) || string.IsNullOrWhiteSpace(rule.Details))
            {
                continue;
            }

            var hit = match == "/"
                ? path == "/"
                : path.Equals(match, StringComparison.OrdinalIgnoreCase) ||
                  path.StartsWith(match + "/", StringComparison.OrdinalIgnoreCase);

            if (hit && match.Length > bestLength)
            {
                best = rule;
                bestLength = match.Length;
            }
        }

        if (best != null)
        {
            return best.Details;
        }

        return string.IsNullOrWhiteSpace(_config.Panel.DefaultDetails)
            ? "В панели OverPanel"
            : _config.Panel.DefaultDetails;
    }

    private string ResolveRoleLabel(string? panelRoleLabel, bool isOwner)
    {
        if (isOwner && !string.IsNullOrWhiteSpace(_config.Identity.OwnerLabel))
        {
            return _config.Identity.OwnerLabel;
        }

        return FirstNonBlank(panelRoleLabel, _config.Identity.RoleLabel);
    }

    private string ResolveRoleTag(string? panelRoleTag, int? rank, bool isOwner)
    {
        if (isOwner && _config.Identity.MaxRank > 0)
        {
            return (_config.Identity.MaxRank + _config.Identity.OwnerRankBonus).ToString();
        }

        if (rank.HasValue)
        {
            return rank.Value.ToString();
        }

        return FirstNonBlank(panelRoleTag, _config.Identity.RoleTag);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>"https://app.overpanel.ru/players?x=1" -> "/players".</summary>
    public static string NormalizePath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "/";
        }

        var value = url.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            value = uri.AbsolutePath;
        }
        else
        {
            var cut = value.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
            {
                value = value[..cut];
            }
        }

        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        if (value.Length > 1 && value.EndsWith('/'))
        {
            value = value.TrimEnd('/');
        }

        return value.Length == 0 ? "/" : value;
    }
}
