using System.Windows;
using Overpanel.Overlay.Services;

namespace Overpanel.Overlay;

public partial class MainWindow
{
    /// <summary>Куда идти после инициализации WebView2: экран входа или сразу панель.</summary>
    private void StartFlow()
    {
        if (_config.Auth.Enabled && _config.Auth.RequireSignIn && !_state.SignedIn)
        {
            ShowLoginPanel();
            return;
        }

        HideLoginPanel();
        NavigateTo(_config.Auth.PanelUrl);
    }

    private void ShowLoginPanel()
    {
        LoginPanel.Visibility = Visibility.Visible;
        BusyBar.Visibility = Visibility.Collapsed;
        SignInButton.IsEnabled = true;
    }

    private void HideLoginPanel()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        BusyBar.Visibility = Visibility.Collapsed;
    }

    private void SignIn_Click(object sender, RoutedEventArgs e) => BeginSignIn();

    private void SkipSignIn_Click(object sender, RoutedEventArgs e)
    {
        _signingIn = false;
        HideLoginPanel();
        NavigateTo(_config.Auth.PanelUrl);
    }

    private void BeginSignIn()
    {
        if (!_webViewReady)
        {
            LoginHint.Text = "WebView2 ещё запускается, подождите пару секунд.";
            return;
        }

        _signingIn = true;
        HideError();
        LoginPanel.Visibility = Visibility.Visible;
        BusyBar.Visibility = Visibility.Visible;
        SignInButton.IsEnabled = false;
        LoginHint.Text = "Открываем страницу входа OverPanel…";

        NavigateTo(_auth.BuildSignInUrl());
    }

    /// <summary>Редирект overpanel-overlay://auth/callback?...</summary>
    private async void HandleAuthCallback(string uriString)
    {
        try
        {
            var uri = new Uri(uriString);
            var token = _auth.TryGetToken(uri);

            AppLog.Info($"SSO callback получен{(string.IsNullOrWhiteSpace(token) ? " без токена" : " с токеном")}");

            if (!string.IsNullOrWhiteSpace(token))
            {
                await ApplyTokenAsync(token);
            }

            await CompleteSignInAsync();
        }
        catch (Exception ex)
        {
            AppLog.Exception("Ошибка обработки SSO callback", ex);
            _signingIn = false;
            BusyBar.Visibility = Visibility.Collapsed;
            SignInButton.IsEnabled = true;
            LoginHint.Text = "Не удалось обработать ответ входа. Попробуйте войти ещё раз.";
        }
    }

    private async Task CompleteSignInAsync()
    {
        _signingIn = false;
        _state.SignedIn = true;
        _state.LastAuthAt = DateTimeOffset.Now.ToString("O");
        StateStore.Save(_state);

        BusyBar.Visibility = Visibility.Collapsed;
        SignInButton.IsEnabled = true;
        LoginHint.Text = string.Empty;
        HideError();
        LoginPanel.Visibility = Visibility.Collapsed;

        var current = CurrentUrl ?? string.Empty;
        if (!current.StartsWith(_config.Auth.PanelUrl, StringComparison.OrdinalIgnoreCase))
        {
            NavigateTo(_config.Auth.PanelUrl);
        }

        await Task.CompletedTask;
    }

    /// <summary>Кладёт токен в localStorage панели, если это настроено (auth.storageInjections).</summary>
    private async Task ApplyTokenAsync(string token)
    {
        var core = Browser.CoreWebView2;
        if (core == null)
        {
            return;
        }

        foreach (var script in _auth.BuildStorageInjectionScripts(token))
        {
            try
            {
                await core.ExecuteScriptAsync(script);
                AppLog.Info("Токен SSO записан в localStorage панели");
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Не удалось записать токен: {ex.Message}");
            }
        }
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _state.SignedIn = false;
        _state.LastAuthAt = null;
        StateStore.Save(_state);

        try
        {
            var core = Browser.CoreWebView2;
            if (core != null)
            {
                core.CookieManager.DeleteAllCookies();
                await core.ExecuteScriptAsync(
                    "try { window.localStorage.clear(); window.sessionStorage.clear(); } catch (e) {}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось очистить сессию: {ex.Message}");
        }

        AppLog.Info("Выход из аккаунта");
        ShowLoginPanel();
        LoginHint.Text = "Вы вышли из аккаунта. Войдите снова, чтобы открыть панель.";
    }
}
