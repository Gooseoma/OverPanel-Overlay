using System.Text.Json;

namespace Overpanel.Overlay.Services;

/// <summary>
/// Скрипт, который оверлей внедряет в страницу панели.
/// Панель менять не обязательно: скрипт читает URL, необязательные data-атрибуты
/// и умеет принимать сообщения от самой панели (см. README, контракт интеграции).
/// </summary>
public static class PanelBridge
{
    /// <summary>Ключ, по которому оверлей отличает свои сообщения.</summary>
    public const string SourceKey = "overpanel-overlay";

    public static string? BuildScript(InjectedScriptConfig config)
    {
        if (config is not { Enabled: true })
        {
            return null;
        }

        var options = JsonSerializer.Serialize(new
        {
            detectActionAttributes = config.DetectActionAttributes,
            attributeName = string.IsNullOrWhiteSpace(config.AttributeName) ? "data-overlay-action" : config.AttributeName.Trim(),
            scanIntervalMs = config.ScanIntervalMs,
            selectors = new
            {
                project = config.Selectors.Project ?? string.Empty,
                roleLabel = config.Selectors.RoleLabel ?? string.Empty,
                roleTag = config.Selectors.RoleTag ?? string.Empty,
                displayName = config.Selectors.DisplayName ?? string.Empty,
                rank = config.Selectors.Rank ?? string.Empty,
            },
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return ScriptTemplate.Replace("__OPTIONS__", options).Replace("__SOURCE__", SourceKey);
    }

    private const string ScriptTemplate = """
(() => {
  if (window.__overpanelOverlayInjected) { return; }
  window.__overpanelOverlayInjected = true;

  const opts = __OPTIONS__;
  const source = '__SOURCE__';

  const post = (payload) => {
    try {
      const bridge = window.chrome && window.chrome.webview;
      if (bridge && typeof bridge.postMessage === 'function') {
        bridge.postMessage(Object.assign({ source: source }, payload));
      }
    } catch (e) { /* игнорируем: страница может быть в iframe/без WebView2 */ }
  };

  let lastRoute = null;
  let lastContext = null;
  let lastAction = null;
  let pending = null;

  const textOf = (selector) => {
    if (!selector) { return null; }
    try {
      const el = document.querySelector(selector);
      if (!el) { return null; }
      const raw = (el.getAttribute('data-rank') || el.textContent || '').trim();
      return raw ? raw.slice(0, 120) : null;
    } catch (e) { return null; }
  };

  const readRoute = () => {
    const path = (location.pathname.replace(/\/+$/, '') || '/');
    if (path === lastRoute) { return; }
    lastRoute = path;
    post({ kind: 'route', path: path, href: location.href, title: document.title });
  };

  const readContext = () => {
    const project = textOf(opts.selectors.project);
    const roleLabel = textOf(opts.selectors.roleLabel);
    const roleTag = textOf(opts.selectors.roleTag);
    const displayName = textOf(opts.selectors.displayName);
    const rankRaw = textOf(opts.selectors.rank);
    const rank = rankRaw && /^\d+$/.test(rankRaw) ? parseInt(rankRaw, 10) : null;
    const key = JSON.stringify([project, roleLabel, roleTag, displayName, rank]);
    if (key === lastContext) { return; }
    lastContext = key;
    post({ kind: 'context', project, roleLabel, roleTag, displayName, rank });
  };

  const readAction = () => {
    if (!opts.detectActionAttributes) { return; }
    let action = null;
    try {
      const nodes = document.querySelectorAll('[' + opts.attributeName + ']');
      for (let i = nodes.length - 1; i >= 0; i--) {
        const node = nodes[i];
        const value = (node.getAttribute(opts.attributeName) || '').trim();
        if (!value) { continue; }
        const rect = node.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) { continue; }
        action = value;
        break;
      }
    } catch (e) { action = null; }

    if (action === lastAction) { return; }
    lastAction = action;
    post({ kind: 'action', action: action });
  };

  const scan = () => {
    readRoute();
    readContext();
    readAction();
  };

  const schedule = () => {
    if (pending) { return; }
    pending = setTimeout(() => { pending = null; scan(); }, 150);
  };

  const wrapHistory = (name) => {
    const original = history[name];
    if (typeof original !== 'function') { return; }
    history[name] = function () {
      const result = original.apply(this, arguments);
      schedule();
      return result;
    };
  };

  wrapHistory('pushState');
  wrapHistory('replaceState');
  window.addEventListener('popstate', schedule);
  window.addEventListener('hashchange', schedule);

  try {
    if (window.MutationObserver && document.documentElement) {
      new MutationObserver(schedule).observe(document.documentElement, {
        childList: true,
        subtree: true,
        characterData: true,
      });
    }
  } catch (e) { /* игнорируем */ }

  setInterval(scan, opts.scanIntervalMs);
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', scan);
  }
  scan();
})();
""";
}
