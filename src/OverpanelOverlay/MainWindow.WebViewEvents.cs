using System.Windows;
using Microsoft.Web.WebView2.Core;
using Overpanel.Overlay.Services;

namespace Overpanel.Overlay;

public partial class MainWindow
{
    private void Core_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var core = Browser.CoreWebView2;
        if (core == null)
        {
            return;
        }

        _tracker.ApplyNavigation(core.Source, core.DocumentTitle);
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_auth.IsCallbackUrl(e.Uri))
        {
            e.Cancel = true;
            HandleAuthCallback(e.Uri);
            return;
        }

        if (e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            _tracker.ApplyNavigation(e.Uri, null);
            return;
        }

        // Любые прочие схемы (mailto:, steam:, discord:) — во внешний обработчик.
        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private async void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            AppLog.Warn($"Навигация не удалась: {e.WebErrorStatus}, HTTP {e.HttpStatusCode} ({Browser.CoreWebView2?.Source})");

            if (_signingIn)
            {
                BusyBar.Visibility = Visibility.Collapsed;
                SignInButton.IsEnabled = true;
                LoginHint.Text = $"Не удалось открыть страницу входа ({e.WebErrorStatus}). Проверьте соединение и попробуйте снова.";
                return;
            }

            if (TrySchedulePanelRetry(e.WebErrorStatus))
            {
                return;
            }

            ShowError($"Страница не загрузилась: {e.WebErrorStatus}.", runtimeMissing: false);
            return;
        }

        var core = Browser.CoreWebView2;
        if (core == null)
        {
            return;
        }

        _navRetryCount = 0;
        HideError();

        if (!_signingIn)
        {
            LoginPanel.Visibility = Visibility.Collapsed;
        }

        var url = core.Source;
        _tracker.ApplyNavigation(url, core.DocumentTitle);
        AppLog.Debug($"Навигация: {url} — «{core.DocumentTitle}»");

        if (_signingIn && _auth.IsSuccessUrl(url))
        {
            await CompleteSignInAsync();
        }
    }

    /// <summary>
    /// Панель может быть недоступна несколько секунд (деплой, перезапуск) —
    /// делаем до трёх автоматических повторов, потом отдаём управление кнопке «Повторить».
    /// </summary>
    private bool TrySchedulePanelRetry(CoreWebView2WebErrorStatus status)
    {
        if (_navRetryCount >= 3)
        {
            _navRetryCount = 0;
            return false;
        }

        _navRetryCount++;
        AppLog.Info($"Панель не ответила ({status}), повтор {_navRetryCount}/3 через 4 с");
        ShowError($"Страница не загрузилась: {status}. Повторная попытка {_navRetryCount}/3…", runtimeMissing: false);

        _ = RetryNavigationAsync();
        return true;
    }

    private async Task RetryNavigationAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
        }
        catch
        {
            return;
        }

        if (!_webViewReady || _signingIn)
        {
            return;
        }

        NavigateTo(_config.Auth.PanelUrl);
    }

    private void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        AppLog.Warn($"Процесс WebView2 завершился: {e.ProcessFailedKind}");
        _webViewReady = false;
        StopRouteTracking();
        ShowError(
            $"Процесс браузера завершился ({e.ProcessFailedKind}). Нажмите «Повторить», чтобы перезапустить оверлей.",
            runtimeMissing: false);
    }

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        PanelMessage? message;

        try
        {
            message = PanelMessage.TryParse(e.WebMessageAsJson);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"WebMessage: {ex.Message}");
            return;
        }

        if (message == null)
        {
            return;
        }

        switch (message.Kind)
        {
            case "route":
                _tracker.ApplyNavigation(message.Path ?? message.Href, message.Title);
                break;

            case "context":
            case "identity":
                _tracker.ApplyIdentity(
                    message.Project,
                    message.RoleLabel,
                    message.RoleTag,
                    message.Rank,
                    message.IsOwner,
                    message.DisplayName);
                break;

            case "action":
                _tracker.ApplyAction(message.Action);
                break;

            case "auth":
                if (!string.IsNullOrWhiteSpace(message.Token))
                {
                    _ = ApplyTokenAsync(message.Token!);
                }

                break;

            case "navigate":
                if (!string.IsNullOrWhiteSpace(message.Href))
                {
                    NavigateTo(message.Href!);
                }

                break;
        }
    }
}
