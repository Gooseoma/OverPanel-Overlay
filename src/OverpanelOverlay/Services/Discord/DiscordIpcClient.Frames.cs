using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;

namespace Overpanel.Overlay.Services.Discord;

public sealed partial class DiscordIpcClient
{
    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        var header = new byte[8];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await pipe.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

                var opcode = BinaryPrimitives.ReadInt32LittleEndian(header);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));

                if (length < 0 || length > MaxFrameBytes)
                {
                    throw new InvalidDataException($"Некорректный размер кадра: {length}");
                }

                var payload = length == 0 ? Array.Empty<byte>() : new byte[length];
                if (length > 0)
                {
                    await pipe.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
                }

                switch (opcode)
                {
                    case OpPing:
                        await SendFrameAsync(OpPong, payload, cancellationToken).ConfigureAwait(false);
                        break;
                    case OpClose:
                        Log?.Invoke("Discord закрыл IPC-соединение");
                        return;
                    case OpFrame:
                        HandleFrame(payload);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // штатное завершение
        }
        catch (EndOfStreamException)
        {
            Log?.Invoke("Discord закрыл IPC-поток");
        }
        catch (ObjectDisposedException)
        {
            // штатное завершение
        }
        catch (IOException ex)
        {
            Log?.Invoke($"IPC: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Exception("Discord IPC read loop", ex);
        }
        finally
        {
            HandleDisconnected();
        }
    }

    private void HandleFrame(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var evt = root.TryGetProperty("evt", out var evtElement) ? evtElement.GetString() : null;
            var cmd = root.TryGetProperty("cmd", out var cmdElement) ? cmdElement.GetString() : null;

            if (string.Equals(evt, "READY", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("user", out var user) &&
                    user.TryGetProperty("username", out var username))
                {
                    UserName = username.GetString();
                }

                Log?.Invoke($"Discord READY{(string.IsNullOrEmpty(UserName) ? string.Empty : $" (user: {UserName})")}");

                TaskCompletionSource<bool>? readyTcs;
                lock (_stateLock)
                {
                    readyTcs = _readyTcs;
                }

                readyTcs?.TrySetResult(true);
                Ready?.Invoke();
                return;
            }

            if (!string.Equals(evt, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var code = -1;
            var message = string.Empty;

            if (root.TryGetProperty("data", out var errorData))
            {
                if (errorData.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode))
                {
                    code = parsedCode;
                }

                if (errorData.TryGetProperty("message", out var messageElement))
                {
                    message = messageElement.GetString() ?? string.Empty;
                }
            }

            Log?.Invoke($"Discord ошибка{(cmd == null ? string.Empty : $" ({cmd})")}: {code} {message}".Trim());
            CommandError?.Invoke(code, message);

            if (cmd == null)
            {
                TaskCompletionSource<bool>? readyTcs;
                lock (_stateLock)
                {
                    readyTcs = _readyTcs;
                }

                readyTcs?.TrySetResult(false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Exception("Не удалось разобрать кадр Discord IPC", ex);
        }
    }

    private void HandleDisconnected()
    {
        TaskCompletionSource<bool>? readyTcs;
        NamedPipeClientStream? pipe;

        lock (_stateLock)
        {
            if (!IsConnected)
            {
                return;
            }

            IsConnected = false;
            readyTcs = _readyTcs;
            pipe = _pipe;
        }

        readyTcs?.TrySetResult(false);

        try
        {
            pipe?.Dispose();
        }
        catch
        {
            // ignore
        }

        Log?.Invoke("Соединение с Discord потеряно");
        Disconnected?.Invoke();
    }

    private async Task SendFrameAsync(int opcode, byte[] payload, CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe;
        lock (_stateLock)
        {
            pipe = _pipe ?? throw new InvalidOperationException("Discord IPC не подключен");
        }

        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), payload.Length);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await pipe.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (payload.Length > 0)
            {
                await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
