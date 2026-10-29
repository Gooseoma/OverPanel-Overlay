using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;

namespace Overpanel.Overlay.Services.Discord;

/// <summary>
/// Минимальный клиент Discord RPC over IPC: handshake + SET_ACTIVITY.
/// Работает напрямую с \\.\pipe\discord-ipc-{0..9}, без внешних библиотек и без Game SDK.
/// </summary>
public sealed partial class DiscordIpcClient : IAsyncDisposable
{
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;
    private const int OpPing = 3;
    private const int OpPong = 4;
    private const int MaxFrameBytes = 1024 * 1024;
    private const int PipeConnectTimeoutMs = 200;

    /// <summary>
    /// Для SET_ACTIVITY null-поля должны оставаться в JSON: activity:null очищает присутствие.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _stateLock = new();

    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private TaskCompletionSource<bool>? _readyTcs;

    public bool IsConnected { get; private set; }

    /// <summary>Имя пользователя Discord, полученное из READY.</summary>
    public string? UserName { get; private set; }

    public event Action<string>? Log;
    public event Action? Ready;
    public event Action? Disconnected;
    public event Action<int, string>? CommandError;

    public async Task<bool> ConnectAsync(string clientId, CancellationToken cancellationToken)
    {
        await DisconnectAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            Log?.Invoke("clientId не задан");
            return false;
        }

        NamedPipeClientStream? pipe = null;

        for (var index = 0; index < 10 && pipe == null; index++)
        {
            var candidate = new NamedPipeClientStream(
                ".",
                $"discord-ipc-{index}",
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough);

            try
            {
                await Task.Run(() => candidate.Connect(PipeConnectTimeoutMs), cancellationToken).ConfigureAwait(false);
                pipe = candidate;
                Log?.Invoke($"Подключились к discord-ipc-{index}");
            }
            catch (OperationCanceledException)
            {
                candidate.Dispose();
                throw;
            }
            catch (TimeoutException)
            {
                candidate.Dispose();
            }
            catch (Exception ex)
            {
                candidate.Dispose();
                Log?.Invoke($"discord-ipc-{index}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (pipe == null)
        {
            Log?.Invoke("Discord IPC pipe не найден — Discord закрыт?");
            return false;
        }

        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource readCts;

        lock (_stateLock)
        {
            _pipe = pipe;
            _readyTcs = readyTcs;
            IsConnected = true;
            readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readCts = readCts;
        }

        try
        {
            var handshake = JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = clientId });
            await SendFrameAsync(OpHandshake, handshake, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Handshake не отправлен: {ex.Message}");
            await DisconnectAsync().ConfigureAwait(false);
            return false;
        }

        var readTask = Task.Run(() => ReadLoopAsync(pipe, readCts.Token), CancellationToken.None);
        lock (_stateLock)
        {
            _readTask = readTask;
        }

        if (!await WaitReadyAsync(readyTcs, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
        {
            Log?.Invoke("Discord не прислал READY — проверь clientId приложения");
        }

        return true;
    }

    /// <summary>Отправляет активность. null = очистить присутствие.</summary>
    public async Task SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["cmd"] = "SET_ACTIVITY",
            ["nonce"] = Guid.NewGuid().ToString(),
            ["args"] = new Dictionary<string, object?>
            {
                ["pid"] = Environment.ProcessId,
                ["activity"] = activity,
            },
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadOptions);
        await SendFrameAsync(OpFrame, bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        CancellationTokenSource? readCts;
        Task? readTask;
        NamedPipeClientStream? pipe;
        TaskCompletionSource<bool>? readyTcs;

        lock (_stateLock)
        {
            readCts = _readCts;
            readTask = _readTask;
            pipe = _pipe;
            readyTcs = _readyTcs;

            _readCts = null;
            _readTask = null;
            _pipe = null;
            _readyTcs = null;
            IsConnected = false;
        }

        readyTcs?.TrySetResult(false);

        if (readCts != null)
        {
            try
            {
                readCts.Cancel();
            }
            catch
            {
                // ignore
            }
        }

        if (readTask != null)
        {
            try
            {
                await Task.WhenAny(readTask, Task.Delay(500)).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            pipe?.Dispose();
        }
        catch
        {
            // ignore
        }

        readCts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    private async Task<bool> WaitReadyAsync(TaskCompletionSource<bool> tcs, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var delay = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
        if (completed != tcs.Task || tcs.Task.Status != TaskStatus.RanToCompletion)
        {
            return false;
        }

        return tcs.Task.Result;
    }
}
