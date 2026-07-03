using System.Diagnostics;

namespace AIStreamDeck;

/// <summary>
/// Pollt das aktive Vordergrundfenster (~350 ms). In einer Konsolen-App feuert
/// SetWinEventHook nicht (kein Message-Pump) -> bewusst Polling.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Timer _timer;
    private string _lastProcess = "";
    private string _lastTitle = "";

    /// <summary>(Prozessname, Fenstertitel) bei Fokuswechsel.</summary>
    public event Action<string, string>? Changed;

    public ForegroundWatcher()
    {
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(350));
    }

    private void Poll()
    {
        try
        {
            nint hWnd = Native.GetForegroundWindow();
            if (hWnd == 0) return;

            string title = Native.GetWindowTitle(hWnd);
            Native.GetWindowThreadProcessId(hWnd, out uint pid);
            string proc = "";
            if (pid != 0)
            {
                try { proc = Process.GetProcessById((int)pid).ProcessName; }
                catch { /* Prozess kann beendet sein */ }
            }
            if (string.IsNullOrEmpty(proc)) return;

            if (proc != _lastProcess || title != _lastTitle)
            {
                _lastProcess = proc;
                _lastTitle = title;
                Changed?.Invoke(proc, title);
            }
        }
        catch { /* defensiv: Polling nie crashen lassen */ }
    }

    public void Dispose() => _timer.Dispose();
}
