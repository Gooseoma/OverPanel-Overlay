using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Overpanel.Overlay.Interop;
using Overpanel.Overlay.Services;
using Overpanel.Overlay.Services.Discord;

namespace Overpanel.Overlay;

/// <summary>
/// Само окно оверлея: frameless, topmost, WebView2 внутри.
/// Никаких хуков в чужие процессы, инжектов в игру и прав администратора —
/// это обычное отдельное окно поверх остальных (безопасно с EAC).
/// </summary>
public partial class MainWindow : Window
{
    private const int HotkeyId = 0x4F50;

    private readonly OverlayConfig _config;
    private readonly PanelStateTracker _tracker;
    private readonly PresenceService _presence;
    private readonly AuthService _auth;
    private readonly OverlayState _state;

    private HwndSource? _hwndSource;
    private bool _webViewReady;
    private bool _signingIn;
    private bool _closing;

    public MainWindow(OverlayConfig config, PanelStateTracker tracker, PresenceService presence)
    {
        InitializeComponent();

        _config = config;
        _tracker = tracker;
        _presence = presence;
        _auth = new AuthService(config);
        _state = StateStore.Load();

        Title = $"OverPanel Overlay {AppInfo.Version}";
        MinWidth = config.Window.MinWidth;
        MinHeight = config.Window.MinHeight;
        Width = Math.Max(config.Window.MinWidth, _state.Width ?? config.Window.Width);
        Height = Math.Max(config.Window.MinHeight, _state.Height ?? config.Window.Height);
        Topmost = _state.Topmost ?? config.Window.Topmost;

        HwidText.Text = DeviceIdentity.Hwid;

        ApplyStartPosition();
        UpdatePinVisual();
        UpdateFooter();

        _tracker.Changed += OnTrackerChanged;
        _presence.StatusChanged += OnPresenceStatusChanged;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SourceInitialized += MainWindow_SourceInitialized;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        _ = InitializeWebViewAsync();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);
            RegisterHotkey(handle);
        }
        catch (Exception ex)
        {
            AppLog.Exception("Не удалось зарегистрировать горячую клавишу", ex);
        }
    }

    private void RegisterHotkey(IntPtr handle)
    {
        if (!_config.Hotkeys.Enabled)
        {
            return;
        }

        if (!HotkeyParser.TryParse(_config.Hotkeys.Toggle, out var modifiers, out var virtualKey))
        {
            AppLog.Warn($"Не удалось разобрать хоткей «{_config.Hotkeys.Toggle}»");
            return;
        }

        if (!NativeMethods.RegisterHotKey(handle, HotkeyId, modifiers, virtualKey))
        {
            AppLog.Warn($"Хоткей «{_config.Hotkeys.Toggle}» занят другим приложением");
            return;
        }

        AppLog.Info($"Хоткей показа/скрытия: {_config.Hotkeys.Toggle}");
    }

    private void UnregisterHotkey()
    {
        try
        {
            if (_hwndSource == null)
            {
                return;
            }

            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
        catch
        {
            // ignore
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleVisibility();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Reload();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove падает, если кнопка уже отпущена — это нормально
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinVisual();
        AppLog.Info($"Поверх всех окон: {(Topmost ? "вкл" : "выкл")}");
    }

    private void UpdatePinVisual()
    {
        PinButton.Foreground = Topmost
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("MutedBrush");
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private void Reload()
    {
        if (_webViewReady)
        {
            Browser.Reload();
        }
        else
        {
            _ = InitializeWebViewAsync();
        }
    }

    private void OpenInBrowser_Click(object sender, RoutedEventArgs e) => OpenExternal(CurrentUrl ?? _config.Auth.PanelUrl);

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = MenuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnTrackerChanged(object? sender, EventArgs e) => UpdateFooter();

    private void OnPresenceStatusChanged(string status) => UpdateFooter();

    private string? CurrentUrl
    {
        get
        {
            try
            {
                return _webViewReady && Browser.CoreWebView2 != null ? Browser.CoreWebView2.Source : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private void UpdateFooter()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateFooter));
            return;
        }

        var snapshot = _tracker.Current;
        var role = string.IsNullOrWhiteSpace(snapshot.RoleTag)
            ? string.Empty
            : $" · {snapshot.RoleLabel} [{snapshot.RoleTag}]";

        FooterText.Text = $"{_presence.Status} · {snapshot.Details}{role}";
        TitleText.Text = string.IsNullOrWhiteSpace(snapshot.Project)
            ? "OverPanel · Overlay"
            : $"OverPanel · {snapshot.Project}";

        StatusDot.Fill = _presence.Status switch
        {
            "Discord: подключено" => (Brush)FindResource("OkBrush"),
            "Discord: подключаемся…" => (Brush)FindResource("WarnBrush"),
            _ when _presence.Status.Contains("ошибка", StringComparison.OrdinalIgnoreCase) ||
                   _presence.Status.Contains("потеряно", StringComparison.OrdinalIgnoreCase) =>
                (Brush)FindResource("FailBrush"),
            _ when _presence.Status.Contains("не запущен", StringComparison.OrdinalIgnoreCase) =>
                (Brush)FindResource("WarnBrush"),
            _ => (Brush)FindResource("MutedBrush"),
        };
    }

    private void ApplyStartPosition()
    {
        if (_config.Window.RememberPosition && _state.Left.HasValue && _state.Top.HasValue)
        {
            Left = _state.Left.Value;
            Top = _state.Top.Value;

            if (IsOnScreen())
            {
                return;
            }
        }

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 24);
        Top = Math.Max(area.Top, area.Top + 80);
    }

    private bool IsOnScreen()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;

        return Left + Width > left + 80 &&
               Top + Height > top + 80 &&
               Left < right - 80 &&
               Top < bottom - 80;
    }

    private void SaveWindowState()
    {
        if (_config.Window.RememberPosition)
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;

            if (!bounds.IsEmpty && bounds.Width > 100 && bounds.Height > 100)
            {
                _state.Left = bounds.Left;
                _state.Top = bounds.Top;
                _state.Width = bounds.Width;
                _state.Height = bounds.Height;
            }
        }

        _state.Topmost = Topmost;
        StateStore.Save(_state);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        e.Cancel = true;
        _closing = true;

        SaveWindowState();
        UnregisterHotkey();

        try
        {
            await _presence.ShutdownAsync();
        }
        catch (Exception ex)
        {
            AppLog.Exception("Ошибка при остановке RPC", ex);
        }

        Application.Current.Shutdown();
    }

    private void OpenExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось открыть внешнюю ссылку: {ex.Message}");
        }
    }
}
