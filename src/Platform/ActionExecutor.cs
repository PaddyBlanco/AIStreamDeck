using System.Diagnostics;

namespace AIStreamDeck;

/// <summary>
/// Fuehrt typisierte Aktionen sicher aus. KI-Aktionen NUR aus dem bekannten Vokabular;
/// runScript NUR aus dem Repo-scripts-Ordner (Whitelist). Kein Freitext-Shell.
/// </summary>
internal sealed class ActionExecutor(string scriptsDir)
{
    public void Run(DeckAction a)
    {
        try
        {
            switch (a.Type)
            {
                case "openUrl":
                    if (!string.IsNullOrWhiteSpace(a.Url)) OpenShell(a.Url!);
                    break;
                case "focusWindow":
                    if (!string.IsNullOrWhiteSpace(a.ProcessName)) Focus(a.ProcessName!);
                    break;
                case "hotkey":
                    if (!string.IsNullOrWhiteSpace(a.Keys)) Hotkey.Send(a.Keys!);
                    break;
                case "runScript":
                    if (!string.IsNullOrWhiteSpace(a.Script)) RunScript(a.Script!);
                    break;
                default:
                    Console.WriteLine($"[Aktion] verworfen (unbekannt): {a.Type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Aktion] Fehler bei {a.Type}: {ex.Message}");
        }
    }

    public void RunScript(string scriptName)
    {
        // Whitelist: nur Dateiname, nur aus scriptsDir, nur .cmd/.ps1
        string name = Path.GetFileName(scriptName);
        string full = Path.Combine(scriptsDir, name);
        string ext = Path.GetExtension(name).ToLowerInvariant();
        if ((ext != ".cmd" && ext != ".ps1") || !File.Exists(full))
        {
            Console.WriteLine($"[Aktion] runScript verworfen: {scriptName}");
            return;
        }
        OpenShell(full);
    }

    private static void OpenShell(string target)
        => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    private static void Focus(string processName)
    {
        string n = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;
        var proc = Process.GetProcessesByName(n).FirstOrDefault(p => p.MainWindowHandle != 0);
        if (proc is not null) Native.SetForegroundWindow(proc.MainWindowHandle);
    }
}

/// <summary>Sendet eine Tastenkombination (z.B. "Ctrl+Shift+P", "Ctrl+=", "Alt+Left") oder einen
/// Akkord (mehrere durch Leerzeichen/Komma getrennte Kombos nacheinander, z.B. "Ctrl+K Ctrl+D") an die aktive App.</summary>
internal static class Hotkey
{
    /// <summary>Sendet die Kombination bzw. den Akkord. True, wenn alle Teilkombos aufgeloest werden konnten.</summary>
    public static bool Send(string combo)
    {
        // Akkorde: "Ctrl+K Ctrl+D" / "Ctrl+K,Ctrl+C" -> Teilkombos der Reihe nach.
        var chords = combo.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (chords.Length == 0) return false;
        if (chords.Length == 1) return SendCombo(chords[0]);
        bool ok = true;
        foreach (var c in chords) { ok &= SendCombo(c); Thread.Sleep(35); } // kurze Pause zwischen Akkord-Schritten
        return ok;
    }

    private static bool SendCombo(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var mods = new List<byte>();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            byte? m = Modifier(parts[i]);
            if (m is not null) mods.Add(m.Value);
        }
        var key = KeyCode(parts[^1]);
        if (key is null) return false;
        if (key.Value.shift && !mods.Contains((byte)0x10)) mods.Add(0x10); // Zeichen braucht Shift

        foreach (var m in mods) Native.keybd_event(m, 0, 0, 0);
        Native.keybd_event(key.Value.vk, 0, 0, 0);
        Native.keybd_event(key.Value.vk, 0, Native.KEYEVENTF_KEYUP, 0);
        for (int i = mods.Count - 1; i >= 0; i--) Native.keybd_event(mods[i], 0, Native.KEYEVENTF_KEYUP, 0);
        return true;
    }

    private static byte? Modifier(string s) => s.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "strg" => 0x11,
        "alt" => 0x12,
        "shift" => 0x10,
        "win" or "meta" or "super" or "cmd" => 0x5B,
        _ => null,
    };

    private static (byte vk, bool shift)? KeyCode(string s)
    {
        s = s.Trim();
        // F1..F12
        if (s.Length >= 2 && (s[0] is 'F' or 'f') && int.TryParse(s[1..], out int fn) && fn is >= 1 and <= 12)
            return ((byte)(0x70 + (fn - 1)), false);

        byte? named = s.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "esc" or "escape" => 0x1B,
            "tab" => 0x09,
            "space" or "spacebar" => 0x20,
            "backspace" or "back" => 0x08,
            "del" or "delete" or "entf" => 0x2E,
            "ins" or "insert" => 0x2D,
            "home" or "pos1" => 0x24,
            "end" or "ende" => 0x23,
            "pageup" or "pgup" or "bild-hoch" => 0x21,
            "pagedown" or "pgdn" or "bild-runter" => 0x22,
            "up" or "arrowup" or "hoch" => 0x26,
            "down" or "arrowdown" or "runter" => 0x28,
            "left" or "arrowleft" or "links" => 0x25,
            "right" or "arrowright" or "rechts" => 0x27,
            "plus" or "add" => 0xBB,      // OEM_PLUS
            "minus" or "subtract" => 0xBD,// OEM_MINUS
            "comma" => 0xBC,
            "period" or "dot" => 0xBE,
            _ => (byte?)null,
        };
        if (named is not null) return (named.Value, false);

        // Beliebiges Zeichen (=, -, /, ., a, 1, …) via VkKeyScan
        if (s.Length == 1)
        {
            short r = Native.VkKeyScan(s[0]);
            if (r != -1)
            {
                byte vk = (byte)(r & 0xFF);
                bool shift = (r & 0x100) != 0;
                if (vk != 0) return (vk, shift);
            }
        }
        return null;
    }
}
