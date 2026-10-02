namespace Deskverse.Application;

using System.Diagnostics;

/// <summary>Live state of the local API, updated by the host that runs it.</summary>
public sealed class LocalApiState
{
    private readonly object _gate = new();

    private int? _port;

    private bool _listening;

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public int? Port
    {
        get
        {
            lock (_gate)
            {
                return _port;
            }
        }
    }

    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _listening;
            }
        }
    }

    public void SetListening(int port)
    {
        lock (_gate)
        {
            _port = port;
            _listening = true;
            StartedAtUtc ??= DateTimeOffset.UtcNow;
        }
    }

    public void SetStopped()
    {
        lock (_gate)
        {
            _listening = false;
        }
    }
}
