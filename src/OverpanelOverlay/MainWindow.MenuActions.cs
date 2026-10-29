using System.Diagnostics;
using System.IO;
using System.Windows;
using Overpanel.Overlay.Services;

namespace Overpanel.Overlay;

public partial class MainWindow
{
    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        if (!_webViewReady)
        {
            _ = InitializeWebViewAsync();
            return;
        }

        if (_config.Auth.Enabled && _config.Auth.RequireSignIn && !_state.SignedIn)
        {
            StartFlow();
            return;
        }

        Browser.Reload();
    }

    private void InstallRuntime_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
    }

    private void CopyHwid_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DeviceIdentity.Hwid);
            FooterText.Text = "HWID скопирован в буфер обмена";
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Буфер обмена недоступен: {ex.Message}");
        }
    }

    private void OpenProfileFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.UserDataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось открыть папку профиля: {ex.Message}");
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(AppPaths.LogFile))
            {
                Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(AppPaths.LogFile)!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось открыть лог: {ex.Message}");
        }
    }
}
