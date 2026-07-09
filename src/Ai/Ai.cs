using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AIStreamDeck;

/// <summary>Was die KI ueber den aktuellen Arbeitskontext weiss (Stufe 1+2).</summary>
internal sealed record WorkContext(
    string Process, string Title, string? ExePath, string? Url,
    string? GitBranch, IReadOnlyList<string> GitChanged, string? Steering, string? Workspace = null,
    string? RepoPath = null)
{
    /// <summary>Cache-Schluessel: Prozess + grober Titel + grobe URL (ohne Query) + Ordner + Steuer-Prompt.</summary>
    public string CacheKey => $"{Process}|{AiParse.Coarsen(Title)}|{UrlKey(Url)}|{Workspace}|{Steering}";

    /// <summary>Kontext als Text fuer den Prompt.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("Aktives Programm: ").Append(Process);
        if (!string.IsNullOrEmpty(ExePath)) sb.Append(" (").Append(Path.GetFileName(ExePath)).Append(')');
        sb.Append("\nFenstertitel: ").Append(Title);
        if (!string.IsNullOrWhiteSpace(Workspace)) sb.Append('\n').Append(Workspace);
        if (!string.IsNullOrWhiteSpace(Url)) sb.Append("\nBrowser-URL: ").Append(Url);
        if (!string.IsNullOrWhiteSpace(GitBranch))
        {
            sb.Append("\nGit-Branch im Arbeits-Repo: ").Append(GitBranch);
            if (!string.IsNullOrWhiteSpace(RepoPath)) sb.Append(" (Repo-Pfad: ").Append(RepoPath).Append(')');
        }
        if (GitChanged.Count > 0) sb.Append("\nZuletzt geaenderte Dateien: ").Append(string.Join(", ", GitChanged));
        if (!string.IsNullOrWhiteSpace(Steering)) sb.Append("\nVorgabe des Nutzers (UNBEDINGT beachten): ").Append(Steering);
        return sb.ToString();
    }

    private static string UrlKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        string s = url;
        int p = s.IndexOf("://", StringComparison.Ordinal);
        if (p >= 0) s = s[(p + 3)..];
        int q = s.IndexOf('?'); if (q >= 0) s = s[..q];
        return s;
    }
}

/// <summary>KI-Backend fuer Tasten-Vorschlaege + Commit-Messages (austauschbar).</summary>
internal interface IAiBackend
{
    bool Enabled { get; }
    string Name { get; }
    Task<IReadOnlyList<DeckAction>> GetAsync(WorkContext ctx);
    Task<string?> CommitMessageAsync(string diff);
    /// <summary>Generischer Aufruf: Prompt rein, (optional schema-validierter) Text raus.</summary>
    Task<string?> GenerateAsync(string prompt, string? schemaJson = null);
}

internal static class AiFactory
{
    /// <summary>Default: Claude Code (gratis via Claude-Abo/OAuth, keine API-Credits).
    /// AISTREAMDECK_AI=codex -> ChatGPT-Abo (Codex); =anthropic|api -> direkte Anthropic-API (kostet Guthaben).</summary>
    public static IAiBackend Create()
    {
        string pick = (Environment.GetEnvironmentVariable("AISTREAMDECK_AI") ?? "claude").ToLowerInvariant();
        return pick switch
        {
            "codex" => new CodexBackend(),
            "anthropic" or "api" => new SuggestionClient(),
            _ => new ClaudeCodeBackend(),
        };
    }
}

/// <summary>
/// Gemeinsamer Prompt für die adaptiven Tasten — EIN Regelwerk für alle drei Backends,
/// damit die Vorschlagsqualität nicht pro Backend auseinanderläuft. Maßstab: Verleitungstest
/// (siehe README „Vorschlags-Leitlinie").
/// </summary>
internal static class AiPrompts
{
    /// <summary>Regelwerk ohne Formatvorgabe (Format kommt je Backend: Schema bzw. <see cref="KeysJsonTail"/>).</summary>
    public const string KeysGuidance = """
        Du belegst die adaptiven Tasten eines Stream Decks. Der Nutzer ist .NET-/C#-Softwareentwickler und arbeitet schnell und tastaturzentriert — aber die Tasten gelten IMMER für das AKTIVE Programm im Vordergrund, nicht für seinen Beruf.
        APP-BINDUNG (hartes Kriterium, vor allem anderen): Jede Taste muss zum AKTIVEN Programm und der dort sichtbaren Aufgabe passen. Mail-/Kalender-Programm → E-Mail-, Termin- und Organisations-Aktionen; Browser → passend zur offenen Seite; Editor/IDE/Terminal → Entwickler-Abläufe. Entwickler-Befehle (dotnet, git, Builds, Ports) sind AUSSERHALB von Entwicklungswerkzeugen VERBOTEN — im Posteingang hilft kein 'dotnet watch'. Auch eine Nutzer-Vorgabe im Kontext gilt nur, soweit sie zum aktiven Programm passt.
        PHYSIK DES GERÄTS — daran wird JEDE Taste gemessen: Seine Hände liegen auf der Tastatur, ein Griff zum Deck kostet Umgreifen + Blick. Eine Taste verführt nur zum Drücken, wenn sie etwas liefert, das Tastatur und Maus NICHT können:
        1) EIN Druck ersetzt einen ganzen ABLAUF (mehrere Befehle/Klicks/Fenster) — nie nur einen Handgriff.
        2) Sie startet Arbeit NEBENLÄUFIG im eigenen Fenster, während er im aktiven Programm weiterarbeitet.
        3) Sie springt PUNKTGENAU zu einer Ressource, die er sonst per Maus und Suche jagen müsste.
        Tasten-Typen, nach Verführungskraft sortiert:
        - command (PowerShell, öffnet ein sichtbares Fenster): verkette mehrere Schritte mit ';' zu EINEM Ablauf (Port freiräumen + neu starten; bauen + testen + Ergebnis öffnen). Der Befehl startet NICHT im Projektordner — steht im Kontext ein Ordner-/Repo-Pfad, beginne mit 'cd <Pfad>; '. Feld 'command'.
        - openUrl: die EXAKTE Ziel-URL aus dem Kontext — die konkrete Doku-Seite zur gerade sichtbaren Technologie/API/Fehlermeldung, die laufende lokale App/Swagger, das GitHub-Repo/PR. Keine Startseiten. Feld 'url'.
        - focusWindow: Sprung zu einem Begleitprogramm, das erkennbar zum Workflow gehört. Feld 'processName'.
        - hotkey: nur menü-vergrabene Kombos, die man nicht im Kopf hat und die JETZT zur Situation passen; Akkorde mit Leerzeichen ('Ctrl+K Ctrl+D'). In Editoren/IDEs höchstens EINE solche Taste; in Programmen ohne CLI (Mail/Office/Explorer) dürfen es mehrere sein, wenn jede eine echte Klickstrecke ersetzt. NIE: Ctrl+S/C/V/X/Z, F5, Alt+Tab, Tab- oder Fensterwechsel — das ist auf der Tastatur schneller. Feld 'keys'.
        KONKRETHEIT: Jede Taste soll ein Detail aus DIESEM Kontext verbauen (Projekt-/Dateiname, Branch, Technologie, Port, Fehlermeldung). Kontextspezifische Ideen kommen IMMER vor allgemeinen; Tasten, die auf jedem beliebigen Entwickler-PC identisch stehen könnten ('Build', 'Docs', 'Terminal'), sind nur als Lückenfüller auf den letzten Plätzen erlaubt. Kopiere keine Beispiele aus dieser Anweisung, leite alles aus dem Kontext ab.
        Mindestens eine Taste soll überraschen: etwas, von dem er vermutlich nicht weiß, dass es mit EINEM Druck geht.
        'label' = das konkrete Objekt (max ~10 Zeichen, Muster 'Test Api', 'Kill 7060'), nie die Kategorie. 'desc' = Pflicht: EIN deutscher Satz (max ~70 Zeichen), was GENAU passiert — läuft als Lauftext unter dem Label.
        ANZAHL (Pflicht): Liefere IMMER GENAU 9 Tasten — nie weniger, keine Taste bleibt leer. Gehen die kontextspezifischen Ideen aus, füllst du die letzten Plätze mit den nützlichsten allgemeineren Entwickler-Abläufen (weiterhin keine trivialen Hotkeys).
        Reihenfolge: was in DIESER Situation am wahrscheinlichsten sofort gebraucht wird, zuerst; die schwächsten ans Ende (auf kleineren Decks fallen sie hinten runter).
        """;

    /// <summary>Formatvorgabe für Backends ohne Output-Schema (Claude Code).</summary>
    public const string KeysJsonTail = "\nAntworte AUSSCHLIESSLICH als JSON, kein Markdown:\n"
        + "{\"keys\":[{\"label\":\"..\",\"desc\":\"was die Taste genau tut\",\"type\":\"command|hotkey|openUrl|focusWindow\",\"url\":null,\"processName\":null,\"keys\":null,\"command\":null}]}";

    /// <summary>Kontext + Regelwerk (ohne Formatvorgabe).</summary>
    public static string Keys(WorkContext ctx) => ctx.Describe() + "\n\n" + KeysGuidance;

    /// <summary>Prompt fuer die Commit-Message aus dem staged Diff (beide CLI-Backends).</summary>
    public static string CommitMsg(string diff)
        => "Schreibe eine einzige knappe Git-Commit-Message (imperativ, max ~72 Zeichen, "
         + "kein Punkt am Ende). Antworte NUR mit der Message, ohne Anfuehrungszeichen.\n\nStaged Diff:\n"
         + AiParse.Trunc(diff, 8000);

    /// <summary>Haengt die JSON-Formatvorgabe an (Backends ohne natives Output-Schema).</summary>
    public static string WithSchema(string prompt, string? schemaJson) => schemaJson is null ? prompt
        : prompt + "\n\nAntworte AUSSCHLIESSLICH als JSON nach diesem Schema (kein Markdown, kein Text drumherum):\n" + schemaJson;
}

/// <summary>Gemeinsames Parsen/Validieren der KI-Antwort (Sicherheitsgrenze: nur bekannte Typen).</summary>
internal static class AiParse
{
    public static List<DeckAction> Keys(JsonElement keysArray)
    {
        var list = new List<DeckAction>();
        foreach (var k in keysArray.EnumerateArray())
        {
            string type = Str(k, "type");
            string desc = Str(k, "desc");
            if (type == "command") // KI-generiertes PowerShell -> Ausfuehrung gated + mit Bestaetigung (siehe DynPress)
            {
                string cmd = Str(k, "command");
                if (!string.IsNullOrWhiteSpace(cmd)) list.Add(new DeckAction(Str(k, "label"), "command", Command: cmd, Description: desc));
                continue;
            }
            if (type is not ("openUrl" or "hotkey" or "focusWindow")) continue;
            list.Add(new DeckAction(Str(k, "label"), type, Str(k, "url"), null, Str(k, "processName"), Str(k, "keys"), Description: desc));
        }
        return list.Take(9).ToList();
    }

    public static string Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // App-Namen, die als LETZTES Titel-Segment stehen (Editor/Browser) -> nicht als Kontext taugen.
    private static readonly string[] AppTails =
        { "visual studio code", "cursor", "windsurf", "visual studio",
          "google chrome", "microsoft edge", "mozilla firefox", "brave", "opera" };

    public static string Coarsen(string title)
    {
        var parts = title.Split(new[] { " - ", " — " }, StringSplitOptions.RemoveEmptyEntries);
        string s;
        if (parts.Length >= 2 && AppTails.Contains(parts[^1].Trim().ToLowerInvariant()))
            s = parts[^2];  // Segment vor dem App-Namen = Projekt/Seite (sonst kollidieren ALLE Fenster einer App)
        else { int d = title.LastIndexOf(" - ", StringComparison.Ordinal); s = d >= 0 ? title[(d + 3)..] : title; }
        s = s.Trim().ToLowerInvariant();
        return s.Length > 60 ? s[..60] : s;
    }

    public static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "\n…(gekuerzt)";

    public static string Tail(string s) => s.Length <= 300 ? s : s[^300..];

    /// <summary>Erste nicht-leere Zeile (Commit-Message-Antworten der CLIs koennen Zusatzzeilen enthalten).</summary>
    public static string? FirstLine(string? s) => s?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

    /// <summary>Schneidet den JSON-Block heraus (falls die KI Text/Markdown drumherum liefert).</summary>
    public static string ExtractJson(string s)
    {
        int a = s.IndexOf('{'); int b = s.LastIndexOf('}');
        return a >= 0 && b > a ? s[a..(b + 1)] : s;
    }
}

/// <summary>
/// Gemeinsamer CLI-Start der Abo-Backends (codex/claude via cmd.exe): UTF-8-Pipes, Prompt via
/// stdin, 90s-Timeout mit Prozessbaum-Kill. Liefert stdout bei Exit 0, sonst null (geloggt mit tag).
/// </summary>
internal static class Cli
{
    public static async Task<string?> RunAsync(string tag, IReadOnlyList<string> args, string stdin,
        bool stripAnthropicKey = false)
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Ohne explizites UTF-8 laufen die Pipes im OEM-Codepage -> Umlaute aus
            // Fenstertiteln (hin) bzw. in desc-Lauftexten (zurueck) werden zerstoert.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("/c");
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (stripAnthropicKey) psi.Environment.Remove("ANTHROPIC_API_KEY"); // Abo/OAuth erzwingen, nie API-Billing

        Process? p = null;
        try
        {
            p = Process.Start(psi)!;
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
            var drainOut = p.StandardOutput.ReadToEndAsync();
            var drainErr = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await p.WaitForExitAsync(cts.Token);
            await Task.WhenAll(drainOut, drainErr);

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"{tag} exit {p.ExitCode}: {AiParse.Tail(await drainErr)}");
                return null;
            }
            return await drainOut;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"{tag} Timeout (90s).");
            try { p?.Kill(entireProcessTree: true); } catch { } // sonst laeuft die CLI verwaist weiter
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{tag} Fehler: {ex.Message}");
            return null;
        }
        finally { try { p?.Dispose(); } catch { } }
    }
}

/// <summary>
/// Ruft die lokale <c>codex</c>-CLI headless auf (laeuft ueber das ChatGPT-Abo, keine API-Credits).
/// Strukturierte Ausgabe via --output-schema, Antwort via --output-last-message. Prompt ueber stdin.
/// </summary>
internal sealed class CodexBackend : IAiBackend
{
    public bool Enabled => true;
    public string Name => "Codex (ChatGPT-Abo)";

    private readonly ConcurrentDictionary<string, IReadOnlyList<DeckAction>> _cache = new();
    private readonly string _schemaPath;

    public CodexBackend()
    {
        _schemaPath = Path.Combine(Path.GetTempPath(), "aistreamdeck_keys_schema.json");
        File.WriteAllText(_schemaPath, SchemaJson);
    }

    public async Task<IReadOnlyList<DeckAction>> GetAsync(WorkContext ctx)
    {
        if (_cache.TryGetValue(ctx.CacheKey, out var hit)) return hit;

        string prompt = AiPrompts.Keys(ctx) + "\nAntworte ausschließlich gemäß Schema.";
        string? outp = await RunAsync(prompt, _schemaPath);
        if (outp is null) return [];
        try
        {
            using var doc = JsonDocument.Parse(outp);
            var result = AiParse.Keys(doc.RootElement.GetProperty("keys"));
            _cache[ctx.CacheKey] = result;
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KI/codex] Parse-Fehler: {ex.Message}");
            return [];
        }
    }

    public async Task<string?> CommitMessageAsync(string diff)
        => AiParse.FirstLine(await RunAsync(AiPrompts.CommitMsg(diff), null));

    public async Task<string?> GenerateAsync(string prompt, string? schemaJson = null)
    {
        string? schemaPath = null;
        if (schemaJson != null)
        {
            schemaPath = Path.Combine(Path.GetTempPath(), "aistreamdeck_gen_" + Guid.NewGuid().ToString("N") + ".json");
            try { await File.WriteAllTextAsync(schemaPath, schemaJson); } catch { schemaPath = null; }
        }
        try { return (await RunAsync(prompt, schemaPath))?.Trim(); }
        finally { if (schemaPath != null) try { File.Delete(schemaPath); } catch { } }
    }

    private static async Task<string?> RunAsync(string prompt, string? schemaPath)
    {
        string outFile = Path.Combine(Path.GetTempPath(), "aistreamdeck_out_" + Guid.NewGuid().ToString("N") + ".txt");
        var args = new List<string> { "codex", "exec", "--skip-git-repo-check", "--ephemeral",
                                      "--sandbox", "read-only", "--color", "never" };
        if (schemaPath is not null) { args.Add("--output-schema"); args.Add(schemaPath); }
        args.Add("--output-last-message"); args.Add(outFile);
        args.Add("-"); // Prompt aus stdin
        try
        {
            if (await Cli.RunAsync("[KI/codex]", args, prompt) is null) return null;
            return File.Exists(outFile) ? await File.ReadAllTextAsync(outFile) : null;
        }
        finally { try { File.Delete(outFile); } catch { } }
    }

    // OpenAI-Strict: alle Properties in 'required', optionale als nullable.
    private const string SchemaJson = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["keys"],
      "properties": {
        "keys": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["label", "desc", "type", "url", "processName", "keys", "command"],
            "properties": {
              "label": { "type": "string" },
              "desc": { "type": "string" },
              "type": { "type": "string", "enum": ["command", "openUrl", "hotkey", "focusWindow"] },
              "url": { "type": ["string", "null"] },
              "processName": { "type": ["string", "null"] },
              "keys": { "type": ["string", "null"] },
              "command": { "type": ["string", "null"] }
            }
          }
        }
      }
    }
    """;
}

/// <summary>
/// Ruft die lokale <c>claude</c>-CLI headless auf (<c>-p</c>/--print). Nutzt die Claude-Code-Anmeldung
/// (OAuth/Abo) — **nicht** die API: der API-Key wird aus dem Kindprozess entfernt, sodass keine
/// API-Credits verbraucht werden. Antwort via <c>--output-format json</c>, Nutztext im Feld "result".
/// Kein --output-schema (wie codex) -> Struktur wird per Prompt gefordert und tolerant geparst.
/// </summary>
internal sealed class ClaudeCodeBackend : IAiBackend
{
    public bool Enabled => true;
    public string Name => "Claude Code (Abo)";

    private const string Model = "opus";     // leistungsstaerkstes Modell
    private const string Effort = "medium";  // Kompromiss Qualitaet/Latenz (naechster Regler: high)
    private readonly ConcurrentDictionary<string, IReadOnlyList<DeckAction>> _cache = new();

    public async Task<IReadOnlyList<DeckAction>> GetAsync(WorkContext ctx)
    {
        if (_cache.TryGetValue(ctx.CacheKey, out var hit)) return hit;
        string prompt = AiPrompts.Keys(ctx) + AiPrompts.KeysJsonTail;
        string? outp = await RunAsync(prompt);
        if (outp is null) return [];
        try
        {
            using var doc = JsonDocument.Parse(AiParse.ExtractJson(outp));
            var result = AiParse.Keys(doc.RootElement.GetProperty("keys"));
            _cache[ctx.CacheKey] = result;
            return result;
        }
        catch (Exception ex) { Console.WriteLine($"[KI/claude] Parse-Fehler: {ex.Message}"); return []; }
    }

    public async Task<string?> CommitMessageAsync(string diff)
        => AiParse.FirstLine(await RunAsync(AiPrompts.CommitMsg(diff)));

    public async Task<string?> GenerateAsync(string prompt, string? schemaJson = null)
        => (await RunAsync(AiPrompts.WithSchema(prompt, schemaJson)))?.Trim();

    private static async Task<string?> RunAsync(string prompt)
    {
        string? stdout = await Cli.RunAsync("[KI/claude]", new[]
        {
            "claude", "-p", "--model", Model, "--effort", Effort, "--output-format", "json",
            "--system-prompt", "Du bist ein Antwort-Backend fuer Stream-Deck-Tasten. Liefere NUR die geforderte Ausgabe, ohne Erklaerung, ohne Markdown.",
            "--disallowed-tools", "Bash Edit Write Read WebFetch WebSearch NotebookEdit",
            "--disable-slash-commands",
        }, prompt, stripAnthropicKey: true);
        if (stdout is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(AiParse.ExtractJson(stdout));
            if (doc.RootElement.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True)
            {
                Console.WriteLine($"[KI/claude] is_error: {AiParse.Tail(stdout)}");
                return null;
            }
            return doc.RootElement.TryGetProperty("result", out var r) ? r.GetString() : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KI/claude] Fehler: {ex.Message}");
            return null;
        }
    }
}
