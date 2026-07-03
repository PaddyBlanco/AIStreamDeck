using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace AIStreamDeck;

/// <summary>Liest best-effort die Adressleisten-URL eines Browsers via UI Automation.</summary>
internal static class BrowserUrl
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" };

    private static UIA3Automation? _automation;
    private static readonly object _gate = new();

    public static string? TryGet(nint hwnd, string process)
    {
        if (hwnd == 0 || !Browsers.Contains(process)) return null;
        try
        {
            lock (_gate)
            {
                _automation ??= new UIA3Automation();
                var window = _automation.FromHandle(hwnd);
                if (window is null) return null;

                var edit = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
                var pattern = edit?.Patterns.Value.PatternOrDefault;
                if (pattern is null) return null;

                string val = pattern.Value.Value;
                val = (val ?? "").Trim();
                return string.IsNullOrWhiteSpace(val) ? null : val;
            }
        }
        catch { return null; } // UIA ist fragil -> niemals crashen, einfach kein URL
    }
}
