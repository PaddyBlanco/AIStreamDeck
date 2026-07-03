using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIStreamDeck;

/// <summary>Eine von Claude vorgeschlagene Tasten-Aktion (sicheres Vokabular).</summary>
internal sealed record DeckAction(
    string Label,
    string Type,
    string? Url = null,
    string? Script = null,
    string? ProcessName = null,
    string? Keys = null,
    string? Command = null,        // PowerShell fuer type=="command" (KI-generiert, gated + Bestaetigung)
    string? Description = null);    // kurze Erklaerung, laeuft als Lauftext unter dem Label

/// <summary>
/// Fragt Claude (Haiku 4.5) nach den 9 nuetzlichsten Tasten fuer das aktive Programm.
/// Roh-HTTP gegen /v1/messages mit erzwungenem Tool-Use (schema-validiert = Sicherheitsgrenze).
/// Cache pro (Prozess + grober Titel). API-Key NUR aus ANTHROPIC_API_KEY.
/// </summary>
internal sealed class SuggestionClient : IAiBackend
{
    public string Name => "Anthropic Haiku 4.5";
    private const string Model = "claude-haiku-4-5";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ConcurrentDictionary<string, IReadOnlyList<DeckAction>> _cache = new();
    private readonly string? _apiKey;

    public bool Enabled => _apiKey is not null;

    public SuggestionClient()
    {
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (_apiKey is not null)
        {
            _http.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
    }

    public async Task<IReadOnlyList<DeckAction>> GetAsync(WorkContext ctx)
    {
        if (!Enabled) return [];
        if (_cache.TryGetValue(ctx.CacheKey, out var hit)) return hit;

        try
        {
            var result = await CallAsync(ctx);
            _cache[ctx.CacheKey] = result;
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KI] Fehler: {ex.Message}");
            return [];
        }
    }

    private async Task<IReadOnlyList<DeckAction>> CallAsync(WorkContext ctx)
    {
        var tool = new
        {
            name = "suggest_keys",
            description = "Liefert die Stream-Deck-Tasten fuer den aktuellen Arbeitskontext.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "keys" },
                properties = new
                {
                    keys = new
                    {
                        type = "array",
                        description = "Bis zu 9 Tasten, beste zuerst.",
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "label", "desc", "type" },
                            properties = new
                            {
                                label = new { type = "string", description = "Max ~10 Zeichen, steht auf der Taste." },
                                desc = new { type = "string", description = "Pflicht: EIN deutscher Satz, was genau passiert (max ~70 Zeichen)." },
                                type = new { type = "string", @enum = new[] { "command", "openUrl", "focusWindow", "hotkey" } },
                                url = new { type = "string", description = "Bei openUrl: vollstaendige URL." },
                                processName = new { type = "string", description = "Bei focusWindow: Prozessname." },
                                keys = new { type = "string", description = "Bei hotkey: z.B. 'Ctrl+.' oder Akkord 'Ctrl+K Ctrl+D'." },
                                command = new { type = "string", description = "Bei command: PowerShell-Befehl (ggf. mit 'cd <Pfad>; ' beginnen)." }
                            }
                        }
                    }
                }
            }
        };

        var body = new
        {
            model = Model,
            max_tokens = 2000,
            system = "Du belegst die adaptiven Tasten eines Stream Decks. Befolge die Regeln in der Nachricht exakt "
                   + "und antworte NUR mit dem Tool-Aufruf.",
            tools = new[] { tool },
            tool_choice = new { type = "tool", name = "suggest_keys" },
            messages = new[]
            {
                new { role = "user", content = AiPrompts.Keys(ctx) }
            }
        };

        using var resp = await _http.PostAsJsonAsync("https://api.anthropic.com/v1/messages", body);
        string respBody = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[KI] HTTP {(int)resp.StatusCode}: {respBody}");
            return [];
        }
        using var doc = JsonDocument.Parse(respBody);

        var list = new List<DeckAction>();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() != "tool_use") continue;
            // Gemeinsame Sicherheitsgrenze: nur bekannte Typen, command gated (siehe AiParse.Keys).
            list.AddRange(AiParse.Keys(block.GetProperty("input").GetProperty("keys")));
        }
        return list.Take(9).ToList();
    }

    /// <summary>Erzeugt eine knappe Git-Commit-Message aus dem (gekuerzten) Diff.</summary>
    public async Task<string?> CommitMessageAsync(string diff)
    {
        if (!Enabled) return null;
        var body = new
        {
            model = Model,
            max_tokens = 200,
            system = "Du schreibst eine einzige, knappe Git-Commit-Message (imperativ, max ~72 Zeichen, "
                   + "kein Punkt am Ende). Antworte NUR mit der Message: keine Anfuehrungszeichen, kein Praefix, keine Erklaerung.",
            messages = new[] { new { role = "user", content = "Staged Diff:\n" + Trunc(diff, 8000) } }
        };
        try
        {
            using var resp = await _http.PostAsJsonAsync("https://api.anthropic.com/v1/messages", body);
            string respBody = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[KI] Commit-Msg HTTP {(int)resp.StatusCode}: {respBody}");
                return null;
            }
            using var doc = JsonDocument.Parse(respBody);
            foreach (var b in doc.RootElement.GetProperty("content").EnumerateArray())
                if (b.GetProperty("type").GetString() == "text")
                    return b.GetProperty("text").GetString()?.Trim();
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KI] Commit-Msg Fehler: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GenerateAsync(string prompt, string? schemaJson = null)
    {
        if (!Enabled) return null;
        string content = schemaJson == null
            ? prompt
            : prompt + "\n\nAntworte AUSSCHLIESSLICH als JSON nach diesem Schema (kein Markdown, kein Text drumherum):\n" + schemaJson;
        var body = new
        {
            model = Model,
            max_tokens = 1500,
            messages = new[] { new { role = "user", content } }
        };
        try
        {
            using var resp = await _http.PostAsJsonAsync("https://api.anthropic.com/v1/messages", body);
            string respBody = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) { Console.WriteLine($"[KI] Generate HTTP {(int)resp.StatusCode}"); return null; }
            using var doc = JsonDocument.Parse(respBody);
            foreach (var b in doc.RootElement.GetProperty("content").EnumerateArray())
                if (b.GetProperty("type").GetString() == "text")
                    return b.GetProperty("text").GetString()?.Trim();
            return null;
        }
        catch (Exception ex) { Console.WriteLine($"[KI] Generate Fehler: {ex.Message}"); return null; }
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "\n…(gekuerzt)";
}
