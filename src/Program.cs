using System.Collections.Concurrent;
using System.Drawing;
using System.Text.Json;
using AIStreamDeck;
using OpenMacroBoard.SDK;
using StreamDeckSharp;

// ---- Konfiguration ----------------------------------------------------------
// Nichts Maschinen-/Firmenspezifisches im Code: Repo-Wurzel wird von der exe aus gesucht,
// Arbeits-Repo + Dev-Wurzeln kommen aus config/projects.json (lokal, siehe *.example.json).
static string FindRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent!)
        if (Directory.Exists(Path.Combine(d.FullName, "config")) && Directory.Exists(Path.Combine(d.FullName, "scripts")))
            return d.FullName;
    return Directory.GetCurrentDirectory();
}
string RootDir = FindRoot();
string ScriptsDir = Path.Combine(RootDir, "scripts");
string SteeringPath = Path.Combine(RootDir, "config", "steering.txt");
string ButtonsPath = Path.Combine(RootDir, "config", "buttons.json");
const bool AllowCommands = true; // KI-/PowerShell-command-Buttons erlaubt

string? RepoPath = null;               // Default-Projekt aus projects.json (Git-Tasten)
var devRootList = new List<string>();  // Eltern-Ordner der Projekte (VS-Code-Ordner-Aufloesung)
try
{
    using var cfgDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RootDir, "config", "projects.json")));
    foreach (var proj in cfgDoc.RootElement.GetProperty("projects").EnumerateArray())
    {
        if (!proj.TryGetProperty("path", out var pv) || pv.GetString() is not string path) continue;
        path = Path.GetFullPath(path);
        if (RepoPath is null || (proj.TryGetProperty("default", out var dv) && dv.ValueKind == JsonValueKind.True))
            RepoPath = path;
        if (Path.GetDirectoryName(path) is string parent
            && !devRootList.Contains(parent, StringComparer.OrdinalIgnoreCase)) devRootList.Add(parent);
    }
}
catch (Exception ex) { Console.WriteLine($"[Config] config/projects.json fehlt/unlesbar ({ex.Message}) — Git-/Repo-Tasten inaktiv."); }
if (Path.GetDirectoryName(RootDir) is string rootParent
    && !devRootList.Contains(rootParent, StringComparer.OrdinalIgnoreCase)) devRootList.Add(rootParent);
string[] DevRoots = devRootList.ToArray();

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ---- Probe-Modus ------------------------------------------------------------
if (args.Length > 0 && args[0] == "probe")
{
    if (RepoPath is null) Console.WriteLine("[Git] kein Arbeits-Repo konfiguriert (config/projects.json).");
    else
    {
        var gi = GitStatus.Read(RepoPath);
        Console.WriteLine($"[Git] ok={gi.Ok} branch={gi.Branch} dirty={gi.Dirty} changed={string.Join(",", GitStatus.ChangedFiles(RepoPath, 5))}");
    }
    Console.WriteLine($"[KI] Backend: {AiFactory.Create().Name}");
    using var w0 = new ForegroundWatcher();
    w0.Changed += (p, t) => Console.WriteLine($"[Fokus] {p} — {t} | URL={BrowserUrl.TryGet(Native.GetForegroundWindow(), p) ?? "-"}");
    Console.WriteLine("Beobachte 6s…"); Thread.Sleep(6000); return;
}

// ---- Plus-Probe: Stream Deck + roh ausmessen (HID-Bytes von Tasten/Reglern/Touch) ----
if (args.Length > 0 && args[0] == "plusprobe")
{
    Console.WriteLine("Elgato-HID-Geraete (VID 0x0FD9):");
    foreach (var d in HidSharp.DeviceList.Local.GetHidDevices(0x0FD9))
    {
        int max = -1; try { max = d.GetMaxInputReportLength(); } catch { }
        Console.WriteLine($"  PID=0x{d.ProductID:X4}  maxIn={max}  {SafeName(d)}");
    }

    var plus = HidSharp.DeviceList.Local.GetHidDevices(0x0FD9, 0x0084).FirstOrDefault();
    if (plus is null) { Console.WriteLine("Kein Stream Deck + (PID 0x0084) gefunden. Angesteckt?"); return; }
    if (!plus.TryOpen(out HidSharp.HidStream stream))
    {
        Console.WriteLine("Konnte + nicht oeffnen — vermutlich haelt die Elgato-App das Geraet. Bitte Elgato beenden und erneut versuchen.");
        return;
    }
    using (stream)
    {
        try { var b = new byte[32]; b[0] = 0x03; b[1] = 0x08; b[2] = 60; stream.SetFeature(b); Console.WriteLine("Output-Test: Helligkeit 60% gesetzt (leuchtet das Deck heller?)."); }
        catch (Exception ex) { Console.WriteLine($"Helligkeit-Test Fehler: {ex.Message}"); }

        try { var fw = new byte[32]; fw[0] = 0x05; stream.GetFeature(fw); Console.WriteLine("GetFeature(0x05): " + Convert.ToHexString(fw, 0, 20)); }
        catch (Exception ex) { Console.WriteLine($"GetFeature Fehler: {ex.Message}"); }

        Console.WriteLine("\n>>> JETZT 25 Sekunden: JEDE Taste druecken, JEDEN Regler drehen UND druecken, Touchscreen tippen! <<<");
        Console.WriteLine("(Punkte = lebt/wartet, Hex = empfangenes Event)\n");
        stream.ReadTimeout = 500;
        var buf = new byte[Math.Max(64, plus.GetMaxInputReportLength())];
        var until = DateTime.UtcNow.AddSeconds(25);
        int reports = 0, timeouts = 0;
        while (DateTime.UtcNow < until)
        {
            try
            {
                int n = stream.Read(buf);
                if (n > 0) { reports++; Console.WriteLine("HEX " + Convert.ToHexString(buf, 0, n)); }
            }
            catch (TimeoutException) { timeouts++; Console.Write("."); }
            catch (Exception ex) { Console.WriteLine($"\n[read] {ex.GetType().Name}: {ex.Message}"); break; }
        }
        Console.WriteLine($"\n\nErgebnis: {reports} Events empfangen, {timeouts} Timeouts.");
        if (reports == 0) Console.WriteLine("-> 0 Events: entweder nichts gedrueckt, ODER Elgato-App laeuft noch (Task-Manager: 'StreamDeck' beenden).");
    }
    Console.WriteLine("Fertig. Bitte ALLE HEX-Zeilen (und die Ergebnis-Zeile) hierher kopieren.");
    return;

    static string SafeName(HidSharp.HidDevice d) { try { return d.GetFriendlyName(); } catch { return d.DevicePath; } }
}

// ---- Plus-Info: HID-Struktur analysieren (kein Oeffnen noetig) ----------------
if (args.Length > 0 && args[0] == "plusinfo")
{
    foreach (var d in HidSharp.DeviceList.Local.GetHidDevices(0x0FD9))
    {
        int mi = -1, mo = -1, mf = -1;
        try { mi = d.GetMaxInputReportLength(); } catch { }
        try { mo = d.GetMaxOutputReportLength(); } catch { }
        try { mf = d.GetMaxFeatureReportLength(); } catch { }
        string nm; try { nm = d.GetFriendlyName(); } catch { nm = d.DevicePath; }
        Console.WriteLine($"PID=0x{d.ProductID:X4} in={mi} out={mo} feat={mf} | {nm}");
        try
        {
            var rd = d.GetReportDescriptor();
            foreach (var item in rd.DeviceItems)
                foreach (var r in item.Reports)
                    Console.WriteLine($"    {r.ReportType} id=0x{r.ReportID:X2} len={r.Length}");
        }
        catch (Exception ex) { Console.WriteLine($"    ReportDescriptor: {ex.Message}"); }
    }
    return;
}

// ---- Plus-Test: nativer Treiber (8 Tasten rendern + Regler/Volume) -----------
if (args.Length > 0 && args[0] == "plustest")
{
    var plus = StreamDeckPlus.TryOpen();
    if (plus is null) { Console.WriteLine("Stream Deck + nicht gefunden/offen (Elgato-App beenden?)."); return; }
    using (plus)
    {
        plus.SetBrightness(80);
        for (int k = 0; k < StreamDeckPlus.KeyCount; k++)
        {
            using var bmp = new System.Drawing.Bitmap(StreamDeckPlus.KeySize, StreamDeckPlus.KeySize);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(ColorFx.Hsv(k * 45));
                using var font = new System.Drawing.Font("Segoe UI", 40, System.Drawing.FontStyle.Bold);
                using var sf = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center };
                g.DrawString(k.ToString(), font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(0, 0, StreamDeckPlus.KeySize, StreamDeckPlus.KeySize), sf);
            }
            plus.SetKeyBitmap(k, bmp);
        }

        // LCD-Streifen: KLARTEST komplett gruen (Format pruefen)
        using (var strip = new System.Drawing.Bitmap(StreamDeckPlus.TouchW, StreamDeckPlus.TouchH))
        {
            using (var g = System.Drawing.Graphics.FromImage(strip))
                g.Clear(System.Drawing.Color.FromArgb(0, 200, 0));
            plus.SetTouchStrip(strip);
            Console.WriteLine("LCD-Streifen: sollte jetzt KOMPLETT GRUEN sein.");
        }

        plus.Touched += (t, x, y) => Console.WriteLine($"[Touch] type={t} x={x} y={y} -> Regler {Math.Clamp(x / 200, 0, 3)}");
        plus.KeyChanged += (k, down) => Console.WriteLine($"[Key] {k} {(down ? "DOWN" : "up")}");
        plus.DialPushed += (d, down) => Console.WriteLine($"[Dial] {d} push {(down ? "DOWN" : "up")}");
        plus.DialRotated += (d, delta) =>
        {
            Console.WriteLine($"[Dial] {d} rot {delta:+#;-#;0}");
            if (d == 0) // linker Regler = Systemlautstaerke
            {
                byte vk = (byte)(delta > 0 ? 0xAF : 0xAE); // VOLUME_UP / DOWN
                for (int i = 0; i < Math.Abs(delta); i++)
                {
                    Native.keybd_event(vk, 0, 0, 0);
                    Native.keybd_event(vk, 0, Native.KEYEVENTF_KEYUP, 0);
                }
            }
        };

        Console.WriteLine("plustest laeuft: Zahlen 0–7 auf den Tasten sichtbar? Tasten druecken, Regler drehen/druecken.");
        Console.WriteLine("Linker Regler = Lautstaerke. Strg+C beendet.");
        using var ev = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; ev.Set(); };
        ev.Wait();
        plus.Reset();
    }
    return;
}

Console.WriteLine("AIStreamDeck — Direkt-HID-Uebernahme. Strg+C beendet und gibt das Geraet frei.");

// ---- Geraet erkennen: MK.2/XL via StreamDeckSharp, sonst Stream Deck + nativ --
IDeckHardware hw;
StreamDeckPlus? plusDev = null;
try
{
    var board = StreamDeck.OpenDevice();
    hw = new SharpHardware(board);
    Console.WriteLine($"Deck erkannt: {hw.CountX}x{hw.CountY} (StreamDeckSharp).");
}
catch (StreamDeckSharp.Exceptions.StreamDeckNotFoundException)
{
    plusDev = StreamDeckPlus.TryOpen();
    if (plusDev is null)
    {
        Console.WriteLine("Kein Deck gefunden (MK.2/XL oder Stream Deck +). Angesteckt? Elgato-App beendet?");
        return;
    }
    hw = new PlusHardware(plusDev);
    Console.WriteLine("Deck erkannt: Stream Deck + (nativer Treiber, 8 Tasten + 4 Regler).");
}
hw.SetBrightness(85);
hw.Clear();

var deck = new DeckController(hw);
var exec = new ActionExecutor(ScriptsDir);
using var anim = new KeyAnimator(deck);
IAiBackend suggestions = AiFactory.Create();
var steering = new Steering(SteeringPath);
Console.WriteLine(suggestions.Enabled ? $"KI aktiv: {suggestions.Name}." : $"KI-Backend {suggestions.Name} nicht bereit.");

int totalKeys = deck.CountX * deck.CountY;
var loadColor = Palette.Loading;
int aiGen = 0;
string lastProc = "", lastTitle = "";

// Start-Regenbogen als Lebenszeichen.
for (int f = 0; f < 16; f++)
{
    for (int k = 0; k < totalKeys; k++) deck.Draw(k, "", ColorFx.Hsv((f * 14 + k * 23) % 360));
    Thread.Sleep(60);
}

// ---- Layout ------------------------------------------------------------------
// MK.2/XL: links 2 Spalten Cockpit-Seiten (+ Blaettern-Taste), rechts adaptive KI.
// Stream Deck +: ganze 8 Tasten pro Seite — Seite 0 = KI, Seite 1 = Cockpit,
//                dann Engine-Seiten; blaettern per Regler 4 (rechts).
bool isPlus = hw.IsPlus;
var allKeys = new List<int>();
for (int row = 0; row < deck.CountY; row++)
    for (int col = 0; col < deck.CountX; col++)
        allKeys.Add(deck.KeyId(col, row));

int blatternKey;
List<int> leftSlots, dynKeys;
if (isPlus)
{
    leftSlots = allKeys; dynKeys = allKeys; blatternKey = -1;
}
else
{
    const int leftCols = 2;
    var leftAll = new List<int>();
    dynKeys = new List<int>();
    foreach (var k in allKeys) { if (k % deck.CountX < leftCols) leftAll.Add(k); else dynKeys.Add(k); }
    blatternKey = leftAll[^1];
    leftSlots = leftAll.GetRange(0, leftAll.Count - 1);
}
int gitKeySlot = leftSlots[0];
int gitDoKey = leftSlots.Count > 1 ? leftSlots[1] : -1;
int promptKey = leftSlots.Count > 2 ? leftSlots[2] : -1;
int cockpitPages = isPlus ? 2 : 1; // + : [KI, Cockpit]; sonst [Cockpit]
int leftPage = 0;
int brightness = 85;
DialSkill? dialSkill = null; // KI-Belegung fuer Regler 3 (Stream Deck +)
var dialSkillCache = new ConcurrentDictionary<string, DialSkill?>(); // pro Kontext, wie GetAsync
var dynActions = new DeckAction?[dynKeys.Count];
var dynLock = new object();
IReadOnlyList<DeckAction> lastDynamic = Array.Empty<DeckAction>();

KeyVisual GitDoVisual() => new("up", "Git", "commit+push", Palette.Push, Pulse: true);
KeyVisual PromptVisual() => new("edit", "Prompt", steering.Current.Length > 0 ? "aktiv" : "leer", Color.FromArgb(200, 80, 180));

// Nur in Entwicklungswerkzeugen gehoert der Git-/Repo-Kontext in den Prompt — sonst zieht er
// die Vorschlaege in Richtung Dev-Buttons (z. B. 'dotnet watch' im Outlook-Posteingang).
static bool IsDevTool(string proc) => proc.ToLowerInvariant() is "code" or "cursor" or "windsurf"
    or "devenv" or "rider64" or "ssms" or "windowsterminal" or "wt" or "powershell" or "pwsh" or "cmd";

WorkContext BuildContext(string proc, string title)
{
    nint hwnd = Native.GetForegroundWindow();
    string? exe = null;
    try
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0) exe = System.Diagnostics.Process.GetProcessById((int)pid).MainModule?.FileName;
    }
    catch { }
    bool devTool = IsDevTool(proc);
    var info = devTool && RepoPath is not null ? GitStatus.Read(RepoPath) : default;
    return new WorkContext(proc, title, exe, BrowserUrl.TryGet(hwnd, proc),
        info.Ok ? info.Branch : null,
        devTool && RepoPath is not null ? GitStatus.ChangedFiles(RepoPath, 5) : Array.Empty<string>(),
        steering.Current,
        WorkspaceProbe.Describe(proc, title, DevRoots), info.Ok ? RepoPath : null);
}
WorkContext CurrentContext() => BuildContext(lastProc, lastTitle);

var engine = new ButtonEngine(ButtonsPath, deck, anim, suggestions, exec, steering, CurrentContext, AllowCommands);
int TotalPages() => cockpitPages + engine.PageCount;
string PageName()
{
    if (isPlus) return leftPage == 0 ? "KI" : leftPage == 1 ? "Cockpit" : engine.Page(leftPage - 2).Name;
    return leftPage == 0 ? "Cockpit" : engine.Page(leftPage - 1).Name;
}

// ---- Rendern ----------------------------------------------------------------
void RenderDynamic(IReadOnlyList<DeckAction> actions)
{
    lock (dynLock)
    {
        lastDynamic = actions;
        for (int i = 0; i < dynKeys.Count; i++)
        {
            DeckAction? a = i < actions.Count ? actions[i] : null;
            dynActions[i] = a;
            if (a is null) { anim.Release(dynKeys[i]); deck.Draw(dynKeys[i], "", Palette.Idle); }
            else anim.Set(dynKeys[i], new KeyVisual(GlyphFor(a.Type), a.Label, SubFor(a), Palette.ForType(a.Type), Ki: true));
        }
    }
}
void SetDynamicAll(string label, Color color)
{
    lock (dynLock)
        for (int i = 0; i < dynKeys.Count; i++) { anim.Release(dynKeys[i]); deck.Draw(dynKeys[i], label, color); }
}

bool OnCockpit() => isPlus ? leftPage == 1 : leftPage == 0;

void RefreshGit()
{
    if (!OnCockpit()) return; // Git-Status nur auf der Cockpit-Seite
    if (RepoPath is null)
    { anim.Set(gitKeySlot, new KeyVisual(null, "kein Repo", "projects.json", Palette.Idle)); return; }
    var info = GitStatus.Read(RepoPath);
    string label = info.Ok ? info.Branch : "git n/a";
    string sub = !info.Ok ? "" : (info.Dirty ? "dirty" : "clean")
        + ((info.Ahead > 0 || info.Behind > 0) ? $" ↑{info.Ahead}↓{info.Behind}" : "");
    Color c = !info.Ok ? Palette.Idle : info.Dirty ? Palette.GitDirty
        : (info.Ahead > 0 || info.Behind > 0) ? Palette.Pull : Palette.GitClean;
    anim.Set(gitKeySlot, new KeyVisual(null, label, sub, c, Pulse: info.Ok && info.Dirty));
}

void RenderCockpit()
{
    RefreshGit(); // Slot 0 = Git-Status
    KeyVisual[] rest =
    {
        GitDoVisual(),
        PromptVisual(),
        new KeyVisual("chat", "Claude", "code", Palette.Claude),
        new KeyVisual("play", "Dev-Up", "watch", Palette.DevUp),
    };
    for (int i = 0; i < rest.Length; i++)
        if (i + 1 < leftSlots.Count) anim.Set(leftSlots[i + 1], rest[i]);
    // Rest der Seite leeren (auf dem + hat die Cockpit-Seite mehr Slots als Cockpit-Tasten).
    for (int s = rest.Length + 1; s < leftSlots.Count; s++)
    { anim.Release(leftSlots[s]); deck.Draw(leftSlots[s], "", Palette.Idle); }
}

void RenderKiPage()
{
    if (lastDynamic.Count > 0) RenderDynamic(lastDynamic);
    else for (int i = 0; i < dynKeys.Count; i++)
            anim.Set(dynKeys[i], new KeyVisual(null, suggestions.Enabled ? "…" : "aus", null, Palette.Idle));
}

void RenderLeft()
{
    if (isPlus)
    {
        if (leftPage == 0) RenderKiPage();
        else if (leftPage == 1) RenderCockpit();
        else engine.Render(leftPage - 2, leftSlots);
    }
    else
    {
        if (leftPage == 0) RenderCockpit();
        else engine.Render(leftPage - 1, leftSlots);
    }
    if (blatternKey >= 0)
        anim.Set(blatternKey, new KeyVisual("sync", PageName(), $"{leftPage + 1}/{TotalPages()}", Color.FromArgb(80, 80, 120)));
}

RenderLeft();
if (!isPlus) // MK.2: rechte adaptive Tasten initialisieren (auf dem + macht das RenderLeft)
    for (int i = 0; i < dynKeys.Count; i++)
        anim.Set(dynKeys[i], new KeyVisual(null, suggestions.Enabled ? "…" : "aus", null, Palette.Idle));

using var gitTimer = new Timer(_ => RefreshGit(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

// ---- Git all-in-one ---------------------------------------------------------
async void RunGitSync()
{
    if (RepoPath is null) { Console.WriteLine("[Git] kein Arbeits-Repo konfiguriert (config/projects.json)."); return; }
    anim.Release(gitDoKey);
    deck.Draw(gitDoKey, "…", loadColor);
    try
    {
        var (res, msg) = await GitOps.SyncAsync(RepoPath, suggestions);
        Console.WriteLine($"[Git] {res}: {msg}");
        (string lbl, Color col) = res switch
        {
            GitSync.Ok => ("OK", Color.FromArgb(0, 130, 50)),
            GitSync.Conflict => ("Conflict", Color.FromArgb(210, 120, 0)),
            _ => ("Fehler", Color.FromArgb(160, 30, 30)),
        };
        deck.Draw(gitDoKey, lbl, col);
        await Task.Delay(2600);
    }
    catch (Exception ex) { Console.WriteLine($"[Git] {ex.Message}"); }
    // async void: der Wiederherstell-Teil darf ebenfalls nicht ungefangen werfen.
    try { if (OnCockpit()) { anim.Set(gitDoKey, GitDoVisual()); RefreshGit(); } }
    catch (Exception ex) { Console.WriteLine($"[Git] restore: {ex.Message}"); }
}

// ---- Prompt-Taste -----------------------------------------------------------
void OpenPrompt()
{
    _ = Task.Run(() =>
    {
        steering.Set(InputBox.Show(steering.Current));
        Console.WriteLine($"[Prompt] '{steering.Current}'");
        if (OnCockpit()) anim.Set(promptKey, PromptVisual());
        if (lastProc.Length > 0) TriggerSuggestions(lastProc, lastTitle);
    });
}

// ---- „Neuer Button" (KI erzeugt ButtonDef) ----------------------------------
const string ButtonSchema = """
{
  "type":"object","additionalProperties":false,
  "required":["label","icon","color","kind","command","target","prompt","output","context"],
  "properties":{
    "label":{"type":"string"},
    "icon":{"type":"string","enum":["play","check","up","down","globe","keyboard","window","chat","edit","sync","star","rocket",""]},
    "color":{"type":"string"},
    "kind":{"type":"string","enum":["command","action","generator","ask"]},
    "command":{"type":["string","null"]},
    "target":{"type":["string","null"]},
    "prompt":{"type":["string","null"]},
    "output":{"type":"array","items":{"type":"string","enum":["clipboard","type","marquee","openUrl","runScript","hotkey","runPowershell"]}},
    "context":{"type":"array","items":{"type":"string","enum":["input","selection","clipboard","window","url","git"]}}
  }
}
""";

void OpenNewButton()
{
    _ = Task.Run(async () =>
    {
        string desc = InputBox.Show("");
        if (string.IsNullOrWhiteSpace(desc)) return;
        anim.Release(leftSlots[0]);
        deck.Draw(leftSlots[0], "…", loadColor);

        ButtonDef? def = null;
        string prompt = $"Erzeuge EINE Stream-Deck-Button-Definition fuer diesen Wunsch:\n\"{desc}\"\n"
            + "Regeln: kind='command' wenn ein PowerShell-Befehl noetig ist (command setzen + output [\"runPowershell\"]); "
            + "kind='action' fuer URL/Skript/Hotkey (target setzen + passendes output); "
            + "kind='generator' wenn die KI bei Druck Text erzeugen soll (prompt + output [\"clipboard\"]). "
            + "icon passend waehlen, color als #RRGGBB, label max 10 Zeichen.";
        try
        {
            string? json = await suggestions.GenerateAsync(prompt, ButtonSchema);
            if (json != null) def = JsonSerializer.Deserialize<ButtonDef>(ExtractJson(json),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) { Console.WriteLine($"[Neuer] {ex.Message}"); }

        if (def != null && !string.IsNullOrWhiteSpace(def.Label))
        {
            if (def.Kind == "command") def.Confirm = true;
            string review = $"Label: {def.Label}\nKind: {def.Kind}\n"
                + (def.Command != null ? $"PowerShell:\n{def.Command}\n" : "")
                + (def.Target != null ? $"Ziel: {def.Target}\n" : "")
                + (def.Prompt != null ? $"Prompt: {def.Prompt}\n" : "")
                + "\nDiesen Button erstellen?";
            if (InputBox.Confirm(review)) { engine.Append(def); Console.WriteLine($"[Neuer] '{def.Label}' angelegt."); }
        }
        else Console.WriteLine("[Neuer] keine gueltige Definition.");
        RenderLeft();
    });
}

// KI-Belegung fuer den freien Regler 3 (Hotkeys je aktivem Programm).
const string DialSchema = """
{ "type":"object","additionalProperties":false,
  "required":["label","cw","ccw","push"],
  "properties":{
    "label":{"type":"string"},
    "cw":{"type":["string","null"]},
    "ccw":{"type":["string","null"]},
    "push":{"type":["string","null"]} } }
""";
async Task<DialSkill?> GetDialSkill(WorkContext ctx)
{
    if (dialSkillCache.TryGetValue(ctx.CacheKey, out var cached)) return cached; // kein erneuter Codex-Start
    string prompt = ctx.Describe()
        + "\n\nBelege den freien Drehregler mit einer WIRKLICH nuetzlichen, kontinuierlichen Aktion fuer dieses Programm — "
        + "etwas, das man gern feinfuehlig dreht statt Tastatur (Zielbild: 'cool, das brauch ich staendig'). "
        + "Gib Hotkeys fuer Rechtsdrehen (cw), Linksdrehen (ccw) und Reindruecken (push = sinnvolle Zusatzaktion, oft Reset) "
        + "im Format 'Ctrl+=' bzw. Akkorde 'Ctrl+K Ctrl+D' (oder null), plus kurzes label (max 8 Zeichen). "
        + "Beispiele: Editor->Zoom/Schrift (Ctrl+=, Ctrl+-, push=Ctrl+0); Browser->Zoom (Ctrl+=, Ctrl+-, push=Ctrl+0); "
        + "Vor/Zurueck-Navigation (Alt+Right, Alt+Left). Keine trivialen Tabwechsel.";
    try
    {
        string? json = await suggestions.GenerateAsync(prompt, DialSchema);
        if (string.IsNullOrWhiteSpace(json)) { Console.WriteLine("[Regler3] KI: keine Antwort."); return null; }
        var ds = JsonSerializer.Deserialize<DialSkill>(ExtractJson(json), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        dialSkillCache[ctx.CacheKey] = ds; // Ergebnis (auch null) merken -> nicht bei jedem Titelwechsel neu fragen
        return ds;
    }
    catch (Exception ex) { Console.WriteLine($"[Regler3] Parse-Fehler: {ex.Message}"); return null; }
}

// ---- KI-Kontext-Tasten (rechts) ---------------------------------------------
bool KiVisible() => !isPlus || leftPage == 0; // + : KI nur auf Seite 0

void TriggerSuggestions(string proc, string title)
{
    if (!suggestions.Enabled) return;
    int gen = Interlocked.Increment(ref aiGen);
    bool showKi = KiVisible();
    if (showKi) for (int i = 0; i < dynKeys.Count; i++) anim.Release(dynKeys[i]);

    _ = Task.Run(async () =>
    {
        using var cts = new CancellationTokenSource();
        var animTask = showKi ? Task.Run(async () =>
        {
            int fr = 0;
            while (!cts.IsCancellationRequested && gen == Volatile.Read(ref aiGen))
            {
                int frame = fr % 2;
                // Nur zeichnen, solange die KI-Seite sichtbar ist — sonst nicht die
                // aktuelle Seite (Cockpit/Engine) mit Dinos uebermalen. Pausiert beim Blaettern.
                if (KiVisible())
                    for (int i = 0; i < dynKeys.Count; i++)
                        deck.DrawDino(dynKeys[i], frame, ColorFx.Hsv((fr * 16 + i * 35) % 360));
                fr++;
                try { await Task.Delay(170, cts.Token); } catch { }
            }
        }) : Task.CompletedTask;

        var ctx = BuildContext(proc, title);
        var dialTask = plusDev is not null ? GetDialSkill(ctx) : Task.FromResult<DialSkill?>(null);
        var actions = await suggestions.GetAsync(ctx);
        cts.Cancel();
        try { await animTask; } catch { }

        if (gen != Volatile.Read(ref aiGen)) return;
        Console.WriteLine($"[KI] {actions.Count} Vorschlaege fuer {proc}");
        lastDynamic = actions;
        if (KiVisible())
        {
            if (actions.Count > 0) RenderDynamic(actions);
            else SetDynamicAll("leer", Palette.Idle);
        }

        if (plusDev is not null)
        {
            var ds = await dialTask;
            if (gen == Volatile.Read(ref aiGen) && ds is not null)
            {
                dialSkill = ds;
                UpdateStrip();
                Console.WriteLine($"[Regler3] {ds.Label}: cw={ds.Cw} ccw={ds.Ccw} push={ds.Push}");
            }
        }
    });
}

engine.SetNewButtonHandler(OpenNewButton);

using var watcher = new ForegroundWatcher();
watcher.Changed += (proc, title) =>
{
    Console.WriteLine($"[Fokus] {proc} — {title}");
    lastProc = proc; lastTitle = title;
    TriggerSuggestions(proc, title);
};

void NextPage() { leftPage = (leftPage + 1) % TotalPages(); RenderLeft(); UpdateStrip(); }

void CockpitPress(int slot)
{
    switch (slot)
    {
        case 0: if (RepoPath is not null) exec.Run(new DeckAction("Repo", "openUrl", Url: RepoPath)); break;
        case 1: RunGitSync(); break;
        case 2: OpenPrompt(); break;
        case 3: exec.Run(new DeckAction("Claude", "runScript", Script: "claude-here.cmd")); break;
        case 4: exec.Run(new DeckAction("Dev-Up", "runScript", Script: "dev-up.cmd")); break;
    }
}
void DynPress(int idx)
{
    if (idx < 0 || idx >= dynActions.Length) return;
    DeckAction? a;
    lock (dynLock) a = dynActions[idx];
    if (a is null) return;
    if (a.Type == "command") // KI-generiertes PowerShell: nur wenn erlaubt + mit Bestaetigung im sichtbaren Fenster
    {
        if (AllowCommands && !string.IsNullOrWhiteSpace(a.Command))
            Shell.RunPowershellVisible(a.Command!, confirm: true);
        return;
    }
    exec.Run(a);
}

// ---- Tastendruck ------------------------------------------------------------
hw.KeyChanged += (key, down) =>
{
    if (!down) return;
    if (key == blatternKey) { NextPage(); return; }

    if (isPlus)
    {
        int slot = leftSlots.IndexOf(key);
        if (slot < 0) return;
        if (leftPage == 0) DynPress(slot);           // KI-Seite
        else if (leftPage == 1) CockpitPress(slot);  // Cockpit
        else engine.Press(leftPage - 2, slot, key);  // Engine-Seiten
        return;
    }

    // MK.2/XL: links Cockpit/Engine, rechts adaptive KI
    int leftSlot = leftSlots.IndexOf(key);
    if (leftSlot >= 0)
    {
        if (leftPage == 0) CockpitPress(leftSlot);
        else engine.Press(leftPage - 1, leftSlot, key);
        return;
    }
    DynPress(dynKeys.IndexOf(key));
};

// ---- Stream Deck + : Regler + Display ---------------------------------------
void UpdateStrip()
{
    if (plusDev is null) return;
    (string label, string val, Color col)[] segs =
    {
        ("Lautstärke", "", Palette.DevUp),
        ("Helligkeit", $"{brightness}%", Palette.GitDirty),
        ("KI-Regler", dialSkill?.Label ?? "…", Color.FromArgb(165, 95, 225)),
        ("Seite", $"{leftPage + 1}/{TotalPages()}", Palette.Pull),
    };
    int segW = StreamDeckPlus.TouchW / 4, h = StreamDeckPlus.TouchH;
    using var strip = new Bitmap(StreamDeckPlus.TouchW, h);
    using var g = Graphics.FromImage(strip);
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
    using var lblFont = new Font("Segoe UI", 14, FontStyle.Bold);
    using var valFont = new Font("Segoe UI", 24, FontStyle.Bold);
    using var lblSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
    using var valSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
    using var lblBrush = new SolidBrush(Color.FromArgb(225, 235, 245));
    using var valBrush = new SolidBrush(Color.White);
    for (int i = 0; i < 4; i++)
    {
        var seg = new Rectangle(i * segW, 0, segW - 1, h);
        using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(seg,
            ColorFx.Mix(segs[i].col, Color.Black, 0.78f), ColorFx.Mix(segs[i].col, Color.Black, 0.40f),
            System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            g.FillRectangle(bg, seg);
        using (var acc = new SolidBrush(segs[i].col))
            g.FillRectangle(acc, seg.X, h - 7, seg.Width, 7);
        g.DrawString(segs[i].label, lblFont, lblBrush, new RectangleF(seg.X + 3, 7, seg.Width - 6, 26), lblSf);
        if (segs[i].val.Length > 0)
            g.DrawString(segs[i].val, valFont, valBrush, new RectangleF(seg.X + 3, 34, seg.Width - 6, h - 44), valSf);
    }
    plusDev.SetTouchStrip(strip);
}
if (plusDev is not null)
{
    plusDev.DialRotated += (d, delta) =>
    {
        switch (d)
        {
            case 0: // links = Systemlautstaerke (fix)
                byte vk = (byte)(delta > 0 ? 0xAF : 0xAE);
                for (int i = 0; i < Math.Abs(delta); i++) { Native.keybd_event(vk, 0, 0, 0); Native.keybd_event(vk, 0, Native.KEYEVENTF_KEYUP, 0); }
                break;
            case 1: // Helligkeit
                brightness = Math.Clamp(brightness + delta * 5, 10, 100);
                hw.SetBrightness(brightness); UpdateStrip();
                break;
            case 2: // KI-adaptiv (Regler 3)
                string? hk = delta > 0 ? dialSkill?.Cw : dialSkill?.Ccw;
                Console.WriteLine($"[Regler3] {(delta > 0 ? "rechts" : "links")}: skill={dialSkill?.Label ?? "—"} hotkey='{hk}'");
                if (!string.IsNullOrWhiteSpace(hk))
                {
                    bool ok = false;
                    for (int i = 0; i < Math.Abs(delta); i++) ok = Hotkey.Send(hk!);
                    if (!ok) Console.WriteLine($"[Regler3] Hotkey '{hk}' nicht sendbar.");
                }
                break;
            case 3: // Seite blaettern
                if (delta > 0) NextPage(); else { leftPage = (leftPage - 1 + TotalPages()) % TotalPages(); RenderLeft(); UpdateStrip(); }
                break;
        }
    };
    plusDev.DialPushed += (d, down) =>
    {
        if (!down) return;
        if (d == 3) NextPage();
        else if (d == 2 && dialSkill?.Push is string p && p.Length > 0) Hotkey.Send(p);
    };
    UpdateStrip();
}

// ---- Beenden ----------------------------------------------------------------
using var exitEvt = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exitEvt.Set(); };
exitEvt.Wait();
Console.WriteLine("Beende, gebe Geraet frei…");
hw.Clear();
hw.Dispose();

// ---- Helfer -----------------------------------------------------------------
static string ExtractJson(string s)
{
    int a = s.IndexOf('{'); int b = s.LastIndexOf('}');
    return a >= 0 && b > a ? s[a..(b + 1)] : s;
}

static string? GlyphFor(string type) => type switch
{
    "openUrl" => "globe",
    "hotkey" => "keyboard",
    "focusWindow" => "window",
    "command" => "play",
    _ => null,
};

static string? SubFor(DeckAction a)
{
    // KI-Beschreibung als Lauftext unter dem Namen (damit klar ist, was die Taste tut).
    string? d = string.IsNullOrWhiteSpace(a.Description) ? null : a.Description!.Trim();
    if (d is not null)
    {
        if (a.Type == "command") return "⚠ " + d;                       // Warnung + Erklaerung
        string? terse = a.Type switch                                    // Erklaerung + Mechanismus
        { "hotkey" => a.Keys, "openUrl" => Domain(a.Url), "focusWindow" => a.ProcessName, _ => null };
        return string.IsNullOrWhiteSpace(terse) ? d : d + " · " + terse;
    }
    return a.Type switch // Fallback ohne desc
    {
        "hotkey" => a.Keys,
        "openUrl" => Domain(a.Url),
        "focusWindow" => a.ProcessName,
        "command" => "⚠ PS",
        _ => null,
    };
}

static string? Domain(string? url)
{
    if (string.IsNullOrWhiteSpace(url)) return null;
    string s = url;
    int p = s.IndexOf("://", StringComparison.Ordinal);
    if (p >= 0) s = s[(p + 3)..];
    int slash = s.IndexOf('/');
    if (slash >= 0) s = s[..slash];
    return s.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? s[4..] : s;
}

/// <summary>KI-Belegung fuer einen Drehregler: Hotkeys fuer Rechts/Links-Drehen und Druecken.</summary>
internal sealed record DialSkill(string Label, string? Cw, string? Ccw, string? Push);
