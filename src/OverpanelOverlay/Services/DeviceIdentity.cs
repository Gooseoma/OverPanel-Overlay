using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Overpanel.Overlay.Services;

/// <summary>
/// HWID для привязки устройства (UPDATES.md п.15: «устройство привязывается по HWID»).
/// Считается локально и не содержит ничего, кроме хеша:
/// MachineGuid + серийный номер системного тома + имя машины -> SHA-256.
/// </summary>
public static class DeviceIdentity
{
    private static string? _cached;

    public static string Hwid => _cached ??= Compute();

    private static string Compute()
    {
        var parts = new[]
        {
            ReadMachineGuid() ?? "no-machine-guid",
            ReadSystemVolumeSerial() ?? "no-volume-serial",
            Environment.MachineName ?? "no-machine-name",
        };

        var raw = string.Join('|', parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var hex = Convert.ToHexString(hash)[..32];

        // Читаемый вид: 8 групп по 4 символа.
        var sb = new StringBuilder(39);
        for (var i = 0; i < hex.Length; i += 4)
        {
            if (i > 0)
            {
                sb.Append('-');
            }

            sb.Append(hex, i, 4);
        }

        return sb.ToString();
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MachineGuid недоступен: {ex.Message}");
            return null;
        }
    }

    private static string? ReadSystemVolumeSerial()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            if (!GetVolumeInformation(root, null, 0, out var serial, out _, out _, null, 0))
            {
                return null;
            }

            return serial.ToString("X8");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Серийный номер тома недоступен: {ex.Message}");
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}
