using Avalonia.Threading;
using System.Collections.ObjectModel;

namespace SVL.Avalonia.Services;

public sealed class DebugConsoleService
{
    private const int MaxLines = 800;
    private readonly object _gate = new();
    private readonly Queue<string> _buffer = new();

    public static DebugConsoleService Instance { get; } = new();

    public event Action<string>? LineAdded;

    public event Action? Cleared;

    private DebugConsoleService()
    {
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _buffer.ToList();
        }
    }

    public void Append(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_gate)
        {
            _buffer.Enqueue(line);
            while (_buffer.Count > MaxLines)
            {
                _buffer.Dequeue();
            }
        }

        Dispatcher.UIThread.Post(() => LineAdded?.Invoke(line));
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }

        Dispatcher.UIThread.Post(() => Cleared?.Invoke());
    }
}

internal sealed class DebugTraceListener : System.Diagnostics.TraceListener
{
    private readonly object _gate = new();
    private readonly System.Text.StringBuilder _pending = new();

    public override void Write(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        lock (_gate)
        {
            _pending.Append(message);
        }
    }

    public override void WriteLine(string? message)
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _pending.Append(message);
            }

            var line = _pending.ToString();
            _pending.Clear();
            DebugConsoleService.Instance.Append(line);
        }
    }
}

public static class DebugTraceBootstrapper
{
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        System.Diagnostics.Trace.AutoFlush = true;
        System.Diagnostics.Trace.Listeners.Add(new DebugTraceListener());
    }
}
