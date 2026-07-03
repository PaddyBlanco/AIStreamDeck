using System.Diagnostics;

namespace AIStreamDeck;

/// <summary>Zwischenablage lesen/schreiben via PowerShell (keine WinForms-Abhaengigkeit).</summary>
internal static class Clip
{
    public static string Get()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-NoProfile", "-Command", "Get-Clipboard -Raw" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return o.TrimEnd('\r', '\n');
        }
        catch { return ""; }
    }

    public static void Set(string text)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell")
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-NoProfile", "-Command", "$t=[Console]::In.ReadToEnd(); Set-Clipboard -Value $t" })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            p.WaitForExit(8000);
        }
        catch { }
    }

    /// <summary>Markierung der aktiven App holen: Strg+C senden, kurz warten, Clipboard lesen.</summary>
    public static string GetSelection()
    {
        Hotkey.Send("Ctrl+C");
        Thread.Sleep(140);
        return Get();
    }

    /// <summary>Text in die aktive App tippen: Clipboard setzen, Strg+V senden.</summary>
    public static void TypeIntoActive(string text)
    {
        Set(text);
        Thread.Sleep(60);
        Hotkey.Send("Ctrl+V");
    }
}
