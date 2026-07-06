using System.Drawing;
using System.Text.Json;

namespace AIStreamDeck;

/// <summary>Eine konfigurierbare Taste (aus config/buttons.json).</summary>
internal sealed class ButtonDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Label { get; set; } = "?";
    public string Icon { get; set; } = "";       // Vektor-Icon-Name (Visuals.IconDraw)
    public string Color { get; set; } = "idle";  // "#RRGGBB" oder Palette-Name
    public string Kind { get; set; } = "action";  // generator|ask|mode|command|action|new|monitor
    public string Trigger { get; set; } = "press"; // press|focus|timer:Ns (aktuell nur press)
    public string[] Context { get; set; } = Array.Empty<string>(); // input|selection|clipboard|window|git|url
    public string? Prompt { get; set; }
    public string? Command { get; set; }            // PowerShell/Script (command/runPowershell)
    public string? Target { get; set; }             // url/script/hotkey/process (action-Outputs)
    public string[] Output { get; set; } = Array.Empty<string>(); // clipboard|type|marquee|status|openUrl|runScript|runPowershell|hotkey
    public bool Confirm { get; set; }
}

internal sealed class PageDef
{
    public string Name { get; set; } = "Seite";
    public List<ButtonDef> Buttons { get; set; } = new();
}

internal sealed class ButtonConfig
{
    public List<PageDef> Pages { get; set; } = new();
}

/// <summary>Laedt/rendert/fuehrt konfigurierbare Tasten aus. Cockpit-Seite liegt in Program.</summary>
internal sealed class ButtonEngine
{
    private readonly string _path;
    private readonly DeckController _deck;
    private readonly KeyAnimator _anim;
    private readonly IAiBackend _ai;
    private readonly ActionExecutor _exec;
    private readonly Steering _steering;
    private readonly Func<WorkContext> _context;
    private readonly bool _allowCommands;
    private Action? _onNew;

    public List<PageDef> Pages { get; private set; } = new();

    public ButtonEngine(string path, DeckController deck, KeyAnimator anim, IAiBackend ai,
        ActionExecutor exec, Steering steering, Func<WorkContext> context, bool allowCommands)
    {
        _path = path; _deck = deck; _anim = anim; _ai = ai; _exec = exec;
        _steering = steering; _context = context; _allowCommands = allowCommands;
        Load();
    }

    public void SetNewButtonHandler(Action onNew) => _onNew = onNew;
    public int PageCount => Pages.Count;
    public PageDef Page(int i) => Pages[i];

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var cfg = JsonSerializer.Deserialize<ButtonConfig>(File.ReadAllText(_path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Pages = cfg?.Pages ?? new();
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Engine] buttons.json Fehler: {ex.Message}"); }
        if (Pages.Count == 0) Pages = Defaults();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new ButtonConfig { Pages = Pages },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Console.WriteLine($"[Engine] Speichern Fehler: {ex.Message}"); }
    }

    /// <summary>Haengt einen generierten Button an die „Buttons"-Seite und speichert.</summary>
    public void Append(ButtonDef def)
    {
        var page = Pages.FirstOrDefault(p => p.Name.Equals("Buttons", StringComparison.OrdinalIgnoreCase));
        if (page is null) { page = new PageDef { Name = "Buttons" }; Pages.Add(page); }
        page.Buttons.Add(def);
        Save();
    }

    public void Render(int pageIndex, IReadOnlyList<int> slots)
    {
        var page = Pages[pageIndex];
        for (int i = 0; i < slots.Count; i++)
        {
            bool isNewSlot = page.Name.Equals("Buttons", StringComparison.OrdinalIgnoreCase) && i == 0;
            if (isNewSlot) { _anim.Set(slots[i], new KeyVisual("edit", "Neuer", "Button", Color.FromArgb(200, 80, 180))); continue; }
            int bi = page.Name.Equals("Buttons", StringComparison.OrdinalIgnoreCase) ? i - 1 : i;
            if (bi >= 0 && bi < page.Buttons.Count) _anim.Set(slots[i], Visual(page.Buttons[bi]));
            else { _anim.Release(slots[i]); _deck.Draw(slots[i], "", Palette.Idle); }
        }
    }

    public void Press(int pageIndex, int slot, int keyId)
    {
        var page = Pages[pageIndex];
        bool buttonsPage = page.Name.Equals("Buttons", StringComparison.OrdinalIgnoreCase);
        if (buttonsPage && slot == 0) { _onNew?.Invoke(); return; }
        int bi = buttonsPage ? slot - 1 : slot;
        if (bi < 0 || bi >= page.Buttons.Count) return;
        _ = ExecuteAsync(page.Buttons[bi], keyId, pageIndex, slot);
    }

    private async Task ExecuteAsync(ButtonDef d, int keyId, int pageIndex, int slot)
    {
        try
        {
            if (d.Kind == "mode") { CycleMode(); return; }

            if (d.Kind == "command" || d.Output.Contains("runPowershell"))
            {
                if (!_allowCommands) { Flash(keyId, "aus", Color.FromArgb(110, 110, 110), d); return; }
                Shell.RunPowershellVisible(d.Command ?? d.Target ?? ""); // vorab bei Erstellung geprueft -> ohne Extra-Dialog
                Flash(keyId, "läuft", Color.FromArgb(0, 110, 40), d);
                return;
            }

            string? result = null;
            if (!string.IsNullOrWhiteSpace(d.Prompt))
            {
                _anim.Release(keyId);
                _deck.Draw(keyId, "…", Palette.Loading);
                string prompt = d.Prompt + GatherContext(d.Context);
                result = await _ai.GenerateAsync(prompt, null);
                result = (result ?? "").Trim();
            }
            ApplyOutputs(d, result ?? "", keyId);
            if (!d.Output.Contains("marquee")) Flash(keyId, "OK", Color.FromArgb(0, 110, 40), d);
        }
        catch (Exception ex) { Console.WriteLine($"[Engine] {d.Label}: {ex.Message}"); _anim.Set(keyId, Visual(d)); }
    }

    private void ApplyOutputs(ButtonDef d, string result, int keyId)
    {
        foreach (var o in d.Output)
        {
            switch (o)
            {
                case "clipboard": Clip.Set(result); break;
                case "type": Clip.TypeIntoActive(result); break;
                case "marquee": ShowMarquee(keyId, result, d); break;
                case "openUrl": _exec.Run(new DeckAction(d.Label, "openUrl", Url: d.Target ?? result)); break;
                case "runScript": _exec.RunScript(d.Target ?? result); break;
                case "hotkey": Hotkey.Send(d.Target ?? result); break;
                case "runPowershell": if (_allowCommands) Shell.RunPowershellVisible(d.Command ?? d.Target ?? ""); break;
            }
        }
    }

    private string GatherContext(string[] context)
    {
        if (context.Length == 0) return "";
        var parts = new List<string>();
        WorkContext? wc = null;
        foreach (var c in context)
        {
            switch (c)
            {
                case "input":
                    string ask = InputBox.Show("");
                    if (!string.IsNullOrWhiteSpace(ask)) parts.Add("Anfrage: " + ask);
                    break;
                case "selection":
                    string sel = Clip.GetSelection();
                    if (!string.IsNullOrWhiteSpace(sel)) parts.Add("Markierter Text:\n" + AiParse.Trunc(sel, 4000));
                    break;
                case "clipboard":
                    string cb = Clip.Get();
                    if (!string.IsNullOrWhiteSpace(cb)) parts.Add("Zwischenablage:\n" + AiParse.Trunc(cb, 4000));
                    break;
                case "window": wc ??= _context(); parts.Add($"Aktives Programm: {wc.Process} — {wc.Title}"); break;
                case "url": wc ??= _context(); if (!string.IsNullOrWhiteSpace(wc.Url)) parts.Add("URL: " + wc.Url); break;
                case "git": wc ??= _context(); if (wc.GitBranch != null) parts.Add($"Git-Branch: {wc.GitBranch}, geaendert: {string.Join(", ", wc.GitChanged)}"); break;
            }
        }
        return parts.Count == 0 ? "" : "\n\n" + string.Join("\n", parts);
    }

    private void CycleMode()
    {
        // Achtung: Der Text geht als "Vorgabe des Nutzers (UNBEDINGT beachten)" in den KI-Prompt —
        // er darf dem Verleitungs-Massstab (AiPrompts) nicht widersprechen und muss auf Dev-Tools
        // gescoped sein, sonst erzeugt er Dev-Buttons auch in Outlook & Co.
        string[] modes = { "", "Coding-Modus (gilt nur in Entwicklungswerkzeugen): Abläufe fürs aktuelle Projekt bevorzugen (Build/Test/Run/EF, Ports) — keine Editor-Hotkeys.",
            "Review-Modus (gilt nur in Entwicklungswerkzeugen): Diff/Vergleich/Git/Test bevorzugen.",
            "Debug-Modus (gilt nur in Entwicklungswerkzeugen): Logs, Breakpoints, Prozesse/Ports bevorzugen." };
        int cur = Array.FindIndex(modes, m => m == _steering.Current);
        _steering.Set(modes[(cur + 1 + modes.Length) % modes.Length]);
        Console.WriteLine($"[Modus] -> '{_steering.Current}'");
    }

    private async void Flash(int keyId, string label, Color color, ButtonDef d)
    {
        // async void: nach dem await unbeobachtete Exceptions wuerden den Prozess beenden -> abfangen.
        try
        {
            _anim.Release(keyId);
            _deck.Draw(keyId, label, color);
            await Task.Delay(1600);
            _anim.Set(keyId, Visual(d));
        }
        catch (Exception ex) { Console.WriteLine($"[Engine] Flash {d.Label}: {ex.Message}"); }
    }

    private async void ShowMarquee(int keyId, string text, ButtonDef d)
    {
        try
        {
            string oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (oneLine.Length > 160) oneLine = oneLine[..160];
            _anim.Set(keyId, new KeyVisual(null, oneLine.Length == 0 ? "—" : oneLine, null, ParseColor(d.Color)));
            await Task.Delay(8000);
            _anim.Set(keyId, Visual(d));
        }
        catch (Exception ex) { Console.WriteLine($"[Engine] Marquee {d.Label}: {ex.Message}"); }
    }

    public KeyVisual Visual(ButtonDef d)
        => new(string.IsNullOrEmpty(d.Icon) ? null : d.Icon, d.Label,
               d.Kind is "command" ? "⚠ PS" : null, ParseColor(d.Color),
               Pulse: d.Kind is "command");

    private static Color ParseColor(string c)
    {
        if (!string.IsNullOrWhiteSpace(c) && c[0] == '#' && c.Length == 7
            && int.TryParse(c.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out int r)
            && int.TryParse(c.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out int g)
            && int.TryParse(c.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
            return Color.FromArgb(r, g, b);
        return c?.ToLowerInvariant() switch
        {
            "commit" => Palette.Commit,
            "pull" => Palette.Pull,
            "push" => Palette.Push,
            "claude" => Palette.Claude,
            "devup" => Palette.DevUp,
            "git" => Palette.GitClean,
            "warn" => Color.FromArgb(180, 60, 40),
            _ => Palette.Idle,
        };
    }

    // Eine einzige statische Engine-Seite ("Buttons"): Slot 0 = „Neuer Button", danach die Tools;
    // generierte Buttons haengen hinten an. Auf dem + ergibt das: Seite 1 KI, Seite 2 Cockpit, Seite 3 diese.
    private static List<PageDef> Defaults() => new()
    {
        new PageDef
        {
            Name = "Buttons",
            Buttons =
            {
                new ButtonDef { Id="regex", Label="Regex", Icon="keyboard", Color="#28B9CD", Kind="generator",
                    Context=new[]{"input"}, Output=new[]{"clipboard"},
                    Prompt="Erzeuge NUR den .NET-kompatiblen Regex-Ausdruck (kein Text drumherum) fuer:" },
                new ButtonDef { Id="sql", Label="SQL", Icon="window", Color="#3C78E1", Kind="generator",
                    Context=new[]{"input"}, Output=new[]{"clipboard"},
                    Prompt="Erzeuge NUR ein T-SQL-Statement (SQL Server, kein Text drumherum) fuer:" },
                new ButtonDef { Id="explain", Label="Erklär", Icon="chat", Color="#9659E1", Kind="generator",
                    Context=new[]{"selection"}, Output=new[]{"marquee"},
                    Prompt="Erklaere kurz (max 1 Satz, deutsch) diesen markierten Text/Fehler:" },
                new ButtonDef { Id="ask", Label="Frag KI", Icon="chat", Color="#E6963C", Kind="ask",
                    Context=new[]{"input","window"}, Output=new[]{"marquee"},
                    Prompt="Beantworte kurz (max 1 Satz, deutsch):" },
                new ButtonDef { Id="mode", Label="Modus", Icon="sync", Color="#46AF5A", Kind="mode" },
            }
        },
    };
}
