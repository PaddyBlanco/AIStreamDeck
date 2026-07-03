using System.Diagnostics;

namespace AIStreamDeck;

/// <summary>Dauerhafter Steuer-Prompt fuer die KI-Tasten-Generierung (in Datei, lokal).</summary>
internal sealed class Steering
{
    private readonly string _path;
    public string Current { get; private set; } = "";

    public Steering(string path)
    {
        _path = path;
        try { if (File.Exists(path)) Current = File.ReadAllText(path).Trim(); } catch { }
    }

    public void Set(string? text)
    {
        Current = (text ?? "").Trim();
        try { File.WriteAllText(_path, Current); } catch { }
    }
}

/// <summary>Natives Eingabefenster (via PowerShell-InputBox) — ohne WinForms-Abhaengigkeit im Projekt.</summary>
internal static class InputBox
{
    public static string Show(string current)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[]
            {
                "-NoProfile", "-STA", "-Command",
                "Add-Type -AssemblyName Microsoft.VisualBasic; " +
                "[Console]::Out.Write([Microsoft.VisualBasic.Interaction]::InputBox(" +
                "'Steuer-Prompt fuer die KI-Tasten (leer = loeschen):','AIStreamDeck',$env:AISD_STEER_CUR))"
            })
                psi.ArgumentList.Add(a);
            psi.Environment["AISD_STEER_CUR"] = current ?? "";

            using var p = Process.Start(psi)!;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(180000);
            return outp.Trim();
        }
        catch { return current ?? ""; }
    }

    /// <summary>Ja/Nein-Dialog (PowerShell MessageBox). True = Ja.</summary>
    public static bool Confirm(string message)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[]
            {
                "-NoProfile", "-STA", "-Command",
                "Add-Type -AssemblyName System.Windows.Forms; " +
                "[Console]::Out.Write([System.Windows.Forms.MessageBox]::Show($env:AISD_MSG,'AIStreamDeck — Button erstellen?','YesNo','Warning').ToString())"
            })
                psi.ArgumentList.Add(a);
            psi.Environment["AISD_MSG"] = message;
            using var p = Process.Start(psi)!;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(120000);
            return outp.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
