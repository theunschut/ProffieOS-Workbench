using System.Text;
using Microsoft.JSInterop;

namespace ProffieOS.Workbench.Services;

/// <summary>
/// Handles the low-level command protocol: send queue, response parsing,
/// command tagging (ProffieOS 8.x+), and watchdog.
/// JS calls back into this class via DotNetObjectReference.
/// </summary>
public class SaberCommandService : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly StringBuilder _buffer = new();
    private TaskCompletionSource<string>? _pendingTcs;
    private TaskCompletionSource<string>? _pendingStatusTcs;
    private DotNetObjectReference<SaberCommandService>? _dotnetRef;

    private bool _useTagging = false;
    private int _tagNumber = 0;

    public bool IsConnected { get; private set; }
    public bool UseTagging
    {
        get => _useTagging;
        set => _useTagging = value;
    }

    public event Func<Task>? OnDisconnectedAsync;
    public event Action<string>? OnError;

    public DotNetObjectReference<SaberCommandService> DotNetRef
        => _dotnetRef ??= DotNetObjectReference.Create(this);

    // Injected by SaberConnectionService after connecting
    public Func<byte[], Task>? SendBytesAsync { get; set; }

    public void MarkConnected()
    {
        IsConnected = true;
        _buffer.Clear();
        _tagNumber = 0;
        _useTagging = false;
    }

    [JSInvokable]
    public void OnDataReceived(string data)
    {
        _buffer.Append(data);
        var buf = _buffer.ToString();
        var endIdx = buf.IndexOf("-+=END_OUTPUT=+-", StringComparison.Ordinal);
        if (endIdx < 0) return;

        var full = buf[..endIdx];
        _buffer.Clear();
        _buffer.Append(buf[(endIdx + "-+=END_OUTPUT=+-".Length)..]);

        var beginIdx = full.IndexOf("-+=BEGIN_OUTPUT=+-\n", StringComparison.Ordinal);
        if (beginIdx >= 0)
            full = full[(beginIdx + "-+=BEGIN_OUTPUT=+-\n".Length)..];

        full = full.Replace("\r", "");
        _pendingTcs?.TrySetResult(full);
    }

    [JSInvokable]
    public void OnStatusReceived(string data)
    {
        _pendingStatusTcs?.TrySetResult(data.Trim());
    }

    [JSInvokable]
    public async Task OnDisconnected()
    {
        IsConnected = false;
        Die("Disconnected");
        if (OnDisconnectedAsync is not null)
            await OnDisconnectedAsync.Invoke();
    }

    /// <summary>Sends a password over the status characteristic and waits for "OK".</summary>
    public Task<string> SendPasswordAndWait(string password, Func<string, Task> sendPw)
    {
        _pendingStatusTcs = new TaskCompletionSource<string>();
        var timeout = Task.Delay(2000).ContinueWith(_ => _pendingStatusTcs?.TrySetResult("TIMEOUT"));
        _ = sendPw(password);
        return _pendingStatusTcs.Task;
    }

    /// <summary>
    /// Send a command, returning its response. Retries up to 20x when tagging is active.
    /// </summary>
    public async Task<string> Send(string cmd, bool retry = false)
    {
        if (_useTagging)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                _tagNumber++;
                var tag = _tagNumber;
                var raw = await Send2($"{tag}| {cmd}");
                var (ok, result) = ParseTaggedResponse(raw, tag);
                if (ok) return result;
                if (!retry) return "";
                await Task.Delay(50);
            }
            return "";
        }
        return await Send2(cmd);
    }

    private static (bool ok, string result) ParseTaggedResponse(string raw, int tag)
    {
        var lines = raw.Split('\n');
        var outputLines = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;
            var pipeIdx = line.IndexOf('|');
            if (pipeIdx < 0) break;

            var pre = line[..pipeIdx].Split(',');
            var post = line[(pipeIdx + 1)..];

            if (pre.Length != 3) break;
            if (!int.TryParse(pre[2], out var lineTag) || lineTag != tag) break;
            if (!int.TryParse(pre[1], out var len) || post.Length != len) return (false, "");
            if (!int.TryParse(pre[0], out var lineNum) || lineNum != outputLines.Count + 1) return (false, "");
            outputLines.Add(post);
        }
        return (true, string.Join("\n", outputLines));
    }

    private async Task<string> Send2(string cmd)
    {
        if (SendBytesAsync is null) return "";
        await _lock.WaitAsync();
        try
        {
            _pendingTcs = new TaskCompletionSource<string>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            cts.Token.Register(() => _pendingTcs.TrySetException(new TimeoutException($"Command timeout: {cmd}")));

            var data = Encoding.UTF8.GetBytes(cmd + '\n');
            await SendChunked(data);

            return await _pendingTcs.Task;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Command failed: {ex.Message}");
            return "";
        }
        finally
        {
            _pendingTcs = null;
            _lock.Release();
        }
    }

    private async Task SendChunked(byte[] data)
    {
        for (var i = 0; i < data.Length; i += 20)
        {
            var chunk = data.Skip(i).Take(20).ToArray();
            await SendBytesAsync!(chunk);
        }
    }

    /// <summary>Cancel all pending commands (e.g. on disconnect or timeout).</summary>
    public void Die(string? reason = null)
    {
        _pendingTcs?.TrySetException(new Exception(reason ?? "Connection lost"));
        _pendingTcs = null;
        _pendingStatusTcs?.TrySetResult("ERROR");
        _pendingStatusTcs = null;
        _buffer.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        Die("Disposed");
        _dotnetRef?.Dispose();
        _dotnetRef = null;
        await Task.CompletedTask;
    }
}
