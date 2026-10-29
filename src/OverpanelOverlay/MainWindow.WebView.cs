using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Overpanel.Overlay.Services;

namespace Overpanel.Overlay;

public partial class MainWindow
{
    /// <summary>
    /// Опрос адреса панели: SPA-переходы (history.pushState) не всегда дают
    /// NavigationCompleted, а без injected-скрипта это единственный способ
    /// держать строку RPC в актуальном состоянии.
    /// </summary>
    private DispatcherTimer? _routeTimer;

    /// <summary>Счётчик автоматических повторов загрузки панели.</summary>
    private int _navRetryCount;

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppPaths.WebViewDataDirectory);

            await Browser.EnsureCoreWebView2Async(environment);

            var core = Browser.CoreWebView2;
            if (core == null)
            {
                ShowError("WebView2 не инициализировался.", runtimeMissing: false);
                return;
            }

            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;

            if (!string.IsNullOrWhiteSpace(_config.Panel.UserAgentSuffix))
            {
                core.Settings.UserAgent = $"{core.Settings.UserAgent} {_config.Panel.UserAgentSuffix.Trim()}";
            }

            var script = PanelBridge.BuildScript(_config.Panel.Injected);
            if (script != null)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            }

            core.WebMessageReceived += Core_WebMessageReceived;
            core.NavigationStarting += Core_NavigationStarting;
            core.NavigationCompleted += Core_NavigationCompleted;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.ProcessFailed += Core_ProcessFailed;
            core.SourceChanged += Core_SourceChanged;

            _webViewReady = true;
            HideError();
            StartFlow();
            StartRouteTracking();

            AppLog.Info($"WebView2 готов: {core.Environment.BrowserVersionString}");
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            AppLog.Warn($"WebView2 Runtime не найден: {ex.Message}");
            ShowError(
                "На этом компьютере не установлен Microsoft Edge WebView2 Runtime. Установите его и нажмите «Повторить».",
                runtimeMissing: true);
        }
        catch (Exception ex)
        {
            AppLog.Exception("Не удалось инициализировать WebView2", ex);
            ShowError($"Не удалось запустить WebView2: {ex.Message}", runtimeMissing: true);
        }
    }

    private void NavigateTo(string url)
    {
        try
        {
            if (Browser.CoreWebView2 != null)
            {
                Browser.CoreWebView2.Navigate(url);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось перейти на {url}: {ex.Message}");
        }
    }

    private void ShowError(string text, bool runtimeMissing)
    {
        ErrorText.Text = text;
        ErrorPanel.Visibility = Visibility.Visible;
        InstallRuntimeButton.Visibility = runtimeMissing ? Visibility.Visible : Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
    }

    private void HideError() => ErrorPanel.Visibility = Visibility.Collapsed;

    private void StartRouteTracking()
    {
        if (_routeTimer == null)
        {
            _routeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1.5),
            };
            _routeTimer.Tick += OnRouteTick;
        }

        _routeTimer.Start();
    }

    private void StopRouteTracking()
    {
        if (_routeTimer == null)
        {
            return;
        }

        _routeTimer.Stop();
        _routeTimer.Tick -= OnRouteTick;
        _routeTimer = null;
    }

    private void OnRouteTick(object? sender, EventArgs e)
    {
        if (!_webViewReady)
        {
            return;
        }

        try
        {
            var core = Browser.CoreWebView2;
            if (core == null)
            {
                return;
            }

            _tracker.ApplyNavigation(core.Source, core.DocumentTitle);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Не удалось прочитать адрес панели: {ex.Message}");
        }
    }
}
