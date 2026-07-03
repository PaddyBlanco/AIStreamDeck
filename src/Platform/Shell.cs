using System.Diagnostics;
using System.Text;

namespace AIStreamDeck;

/// <summary>
/// Startet PowerShell-Kommandos in einem SICHTBAREN Fenster. <c>-EncodedCommand</c> (Base64/UTF-16LE)
/// umgeht jedes Cmdline-Quoting-Problem.
/// <para><paramref name="confirm"/>=true fuer **nicht vorab gepruefte** KI-Kommandos: das Fenster zeigt
/// Befehl + Arbeitsverzeichnis und fragt nach — nur 'j'/'y' fuehrt aus, alles andere bricht ab
/// (Default-Deny). Vorab gepruefte Engine-Buttons laufen mit confirm=false direkt.</para>
/// </summary>
internal static class Shell
{
    public static void RunPowershellVisible(string command, bool confirm = false)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        try
        {
            string script = confirm ? ConfirmWrap(command) : command;
            string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo("powershell",
                "-NoExit -NoProfile -EncodedCommand " + b64) { UseShellExecute = true });
        }
        catch (Exception ex) { Console.WriteLine($"[PS] {ex.Message}"); }
    }

    /// <summary>Umschliesst den KI-Befehl mit Anzeige + Ja/Nein-Abfrage im Fenster (Default-Deny).</summary>
    private static string ConfirmWrap(string command)
    {
        string lit = "'" + command.Replace("'", "''") + "'"; // als PS-Stringliteral (Hochkommas verdoppeln)
        return
            "Write-Host 'KI-Vorschlag (nicht vorab geprueft):' -ForegroundColor Yellow; " +
            "Write-Host ('  ' + " + lit + ") -ForegroundColor Cyan; " +
            "Write-Host ('  Verzeichnis: ' + (Get-Location).Path) -ForegroundColor DarkGray; " +
            "if ((Read-Host 'Ausfuehren? [j/N]') -match '^(j|y)') { Invoke-Expression " + lit + " } " +
            "else { Write-Host 'Abgebrochen.' -ForegroundColor DarkGray }";
    }
}
