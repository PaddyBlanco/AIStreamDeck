# AIStreamDeck — Live-Steuerung des Arbeits-Stream-Decks

Lokales .NET-Programm, das ein Stream Deck direkt über **USB-HID** übernimmt und die Tasten
mit **Live-Inhalt** versorgt (Text/Farbe/Icons in Echtzeit, Tastendruck löst C#-Code aus).
Läuft auf **zwei Geräten** mit **gemeinsamer Basis-Logik**:

- **Stream Deck 2 / MK.2** (Model `20GAA9901`, 15 Tasten) — über `StreamDeckSharp`.
- **Stream Deck +** (Model `20GBD9901`, 8 Tasten + 4 Drehregler + Touch-LCD) — über einen
  **eigenen nativen HID-Treiber** ([Devices/StreamDeckPlus.cs](Devices/StreamDeckPlus.cs)), da
  StreamDeckSharp das + nicht unterstützt.

> Status: **lauffähig** (baut, `probe` verifiziert). Auf dem MK.2 device-getestet; die
> +-Vollansicht (Seiten, Regler, LCD) ist implementiert, der volle Gerätetest am + steht noch aus.

## Geräte-Erkennung & Abstraktion

Beim Start wird das Gerät erkannt und hinter der Schnittstelle **`IDeckHardware`**
([Devices/DeckHardware.cs](Devices/DeckHardware.cs)) gekapselt:

| Gerät | Adapter | Quelle |
|---|---|---|
| MK.2 / XL / Mini / Original | `SharpHardware` | `StreamDeckSharp` |
| Stream Deck + | `PlusHardware` | nativer Treiber `StreamDeckPlus.cs` |

**Alles oberhalb der Schnittstelle ist geräteunabhängig:** `DeckController` (Zeichnen),
`ButtonEngine`, `Visuals` (Icons/Farben/Dino), `Ai`, `GitOps`, `GitStatus`. Nur die
+-Extras (Regler, LCD, Seiten-Paging) liegen bewusst inline in `Program.cs`.

## Layout

**MK.2 (5×3):** linke 2 Spalten = Cockpit (blätterbar), rechte 3 Spalten = 9 adaptive KI-Tasten.
```
[STATIK][STATIK] | [KI][KI][KI]
[STATIK][STATIK] | [KI][KI][KI]
[STATIK][BLÄTT.] | [KI][KI][KI]
```

**Stream Deck + (2×4):** die ganzen 8 Tasten bilden **eine Seite**, gewechselt mit dem
**rechten Drehregler (Regler 4)** — **3 Seiten**:
- **Seite 1 = KI** (adaptive Vorschläge, **roter Rand unten** zur Kennzeichnung).
- **Seite 2 = Cockpit** (statisch): Git-Status · Git commit+pull+push · Prompt · Claude · Dev-Up.
- **Seite 3 = Tools** (statisch): ＋Neuer Button · Regex · SQL · Erklär · Frag-KI · Modus
  (+ per „Neuer Button" erzeugte). Freie Slots über `config/buttons.json` befüllbar.

**Cockpit-Tasten:** Git-Status (live, öffnet auf Druck das Repo) · Git commit+pull+push ·
Prompt · Claude-Code · Dev-Up.

## Drehregler & LCD (nur Stream Deck +)

| Regler | Funktion |
|---|---|
| 1 (links) | **Systemlautstärke** (fix, `VK_VOLUME_UP/DOWN`) |
| 2 | Deck-Helligkeit |
| 3 | **KI-adaptiver Skill** — Hotkeys je aktivem Programm (Editor→Schriftgröße, Browser→Tabs …); Druck = zusätzliche Aktion |
| 4 (rechts) | **Seite blättern** (Druck = nächste Seite) |

Der Touch-LCD-Streifen (800×100) zeigt 4 Segmente: Lautstärke · Helligkeit · KI-Regler · Seite.

## KI-Backend (umschaltbar)

Standard: **Claude Code** (`claude -p`) — läuft über dein **Claude-Code-Abo (OAuth)**, **keine
API-Credits**. Der API-Key wird dem Kindprozess entzogen (`ANTHROPIC_API_KEY` entfernt), damit
garantiert das Abo und **nicht** die API genutzt wird. Voraussetzung: einmal `claude login` (Abo).
Modell **`opus`** mit **`--effort medium`** (Kompromiss Qualität/Latenz; nächster Regler wäre
`high`); kein `--output-schema` (anders als codex) → Struktur wird per Prompt gefordert und tolerant
geparst (`.result` aus `--output-format json`). Die Pipes zu den CLI-Kindprozessen laufen explizit
**UTF-8** — sonst garbeln Umlaute aus Fenstertiteln (hin) bzw. in `desc`-Lauftexten (zurück).

Umschalten über `AISTREAMDECK_AI`:
- `claude` (Default) — Claude-Code-Abo.
- `codex` — Codex-CLI über das **ChatGPT-Abo** (`codex login`), ebenfalls ohne API-Credits.
- `anthropic` / `api` — direkte Anthropic-API (`ANTHROPIC_API_KEY`, **kostet API-Guthaben**).

> **Achtung Nutzungslimit:** Das Claude-Abo teilt sich das Kontingent mit deiner **interaktiven**
> Claude-Code-Arbeit. Der Deck-Autofire pro Fensterwechsel kann das 5h-Limit belasten (Caching
> mildert). Wer die Pools trennen will: `AISTREAMDECK_AI=codex` (eigener ChatGPT-Pool).

An das KI-Backend gehen **nur Prozessname + Fenstertitel** (+ ggf. Browser-URL sowie lokale
**Ordner-/Repo-Pfade**, damit `command`-Vorschläge per `cd` im richtigen Verzeichnis starten), und —
**nur beim Commit** — der gekürzte staged Diff des Arbeits-Repos. Keine Screenshots/Fensterinhalte.

**Editor-Ordner-Kontext (VS Code/Cursor):** Damit die Vorschläge zum offenen Projekt passen, ermittelt
`WorkspaceProbe` best-effort den offenen Ordner und beschreibt **kompakt** seine Struktur (Projekttyp,
`.sln`/`.csproj`, Unterordner-Namen — **keine Dateiinhalte**). VS Code gibt den Pfad nicht direkt raus;
die Auflösung läuft über die **Dev-Wurzeln** (abgeleitet aus den Eltern-Ordnern der Projektpfade in
`config/projects.json`) per Ordnername aus dem Titel. **Optional 100 % zuverlässig:** in VS Code
`settings.json` den Titel den Pfad zeigen lassen, z. B. `"window.title": "${activeEditorShort}${separator}${rootPath}"`
— dann liest die App den echten Pfad direkt aus dem Titel.

### Vorschlags-Leitlinie (Verleitungstest)

Die adaptiven Tasten folgen einem strengen Maßstab — Prompt zentral in **`AiPrompts`**
([Ai/Ai.cs](Ai/Ai.cs)), **gemeinsam für alle drei Backends**. Ausgangspunkt ist die **Physik des
Geräts**: Die Hände liegen auf der Tastatur, ein Griff zum Deck kostet Umgreifen + Blick. Eine Taste
verführt nur, wenn sie etwas liefert, das Tastatur/Maus **nicht** können:

- **Ein Druck = ganzer Ablauf**: PowerShell-Ketten (`cd <Projekt>; dotnet test; …`) statt
  Einzel-Handgriffe — mehrere Befehle/Klicks/Fenster in einem Druck.
- **Nebenläufig**: Build/Tests/Watch starten im eigenen Fenster, während man im Editor weitertippt.
- **Punktgenauer Absprung**: die exakte Doku-/Swagger-/PR-URL statt Maus-Suche; keine Startseiten.
- **Konkretheit (hartes Kriterium)**: jede Taste muss ein Detail aus dem Kontext verbauen (Projekt,
  Datei, Branch, Port, Fehlermeldung). Tasten, die auf jedem Entwickler-PC identisch stünden
  („Build", „Docs"), sind Füllmaterial → weglassen. Beispiele aus dem Prompt dürfen nicht kopiert
  werden. Mindestens eine Taste soll überraschen („Was, das geht?!").
- **Hotkeys stark begrenzt**: höchstens einer, nur menü-vergrabene Akkorde, die zur Situation passen;
  triviale Shortcuts (Ctrl+S/C/V, F5, Alt+Tab, Tabwechsel) sind verboten — Tastatur ist schneller.
- Jede Taste liefert ein Pflicht-**`desc`** (1 Satz, was sie tut) → läuft als **Lauftext unter dem
  Namen**. PowerShell-Tasten zeigen „⚠" davor.
- Damit `cd` möglich ist, enthält der Kontext die **Pfade** des aufgelösten Editor-Ordners und des
  Arbeits-Repos (weiterhin nur Namen/Struktur, **nie** Dateiinhalte).

Der **Modus**-Button setzt den Steuer-Prompt: Der frühere Coding-Modus („Editor-Hotkeys bevorzugen")
stand dem Maßstab direkt entgegen und heißt jetzt „Abläufe fürs aktuelle Projekt, keine
Editor-Hotkeys".

## Konfigurierbare Button-Engine

`config/buttons.json` (lokal, nie in Git) beschreibt **Seiten** aus `ButtonDef`s
([Buttons/ButtonEngine.cs](Buttons/ButtonEngine.cs)). Fehlt die Datei, greifen Defaults:

- **KI-Tools:** Regex · SQL · Erklär (Markierung) · Frag KI · Modus (Coding/Review/Debug).
- **Buttons:** „＋ Neuer Button" + die per KI generierten Tasten.

`kind` = `generator` (Prompt→Ergebnis in Zwischenablage/Lauftext) · `ask` · `mode` ·
`action` (openUrl/hotkey/runScript) · `command` (PowerShell). Kontext (`input`, `selection`,
`clipboard`, `window`, `url`, `git`) wird bei Druck gesammelt und dem Prompt angehängt.

### „Neuer Button" (KI erzeugt eine Taste)
Druck → Beschreibung eingeben → Dino-Ladeanimation → KI liefert eine `ButtonDef` (Struktur-Schema,
darf einen PowerShell-Command enthalten) → **Bestätigungs-Dialog** → wird an die „Buttons"-Seite
angehängt und sofort gerendert.

## Modi (Kommandozeile)

```
dotnet run -- probe        # ohne Deck: Git + Fokus-Erkennung + Backend testen
dotnet run -- plusprobe    # Stream Deck + roh ausmessen (HID-Bytes)
dotnet run -- plusinfo     # HID-Report-Struktur anzeigen (kein Öffnen nötig)
dotnet run -- plustest     # nativer +-Treiber: 8 Tasten + Regler + Volume + LCD
dotnet run                 # Hauptprogramm (Deck übernehmen; Elgato-App vorher beenden!)
# Backend wählen (Default = claude/Abo):  $env:AISTREAMDECK_AI="claude" | "codex" | "anthropic"; dotnet run
```

## Abhängigkeiten

| Paket | Zweck | Lizenz |
|---|---|---|
| `StreamDeckSharp` 10.0.0 | HID-Zugriff MK.2/XL (zeichnen/lesen) | MIT (unofficial) |
| `System.Drawing.Common` 9.0.0 | Text/Grafik/Icons auf Tasten rendern (GDI+) | MIT |
| `HidSharp` 2.6.4 | roher HID-Zugriff für den nativen +-Treiber | MIT (Apache-Teile) |
| `FlaUI.UIA3` 5.0.0 | Browser-URL best-effort via UI Automation | MIT |

Kein `SixLabors.ImageSharp.Drawing` (3.x = kommerzielle Pflichtlizenz → ausgeschlossen).
Keine Abhängigkeiten auf das Arbeits-Repo.

## Sicheres Aktions-Schema (KI darf NUR diese Typen liefern)

| Typ | Felder | Risiko |
|---|---|---|
| `openUrl` | `url` | gering |
| `hotkey` | `keys` (an Vordergrund-App) | gering |
| `focusWindow` | `processName` | gering |
| `runScript` | `script` (**nur** aus `..\scripts\`-Whitelist) | mittel |
| `command` / `runPowershell` | `command` (PowerShell) | **hoch** — siehe unten |

- **Adaptive KI-Tasten:** `openUrl`/`hotkey`/`focusWindow` **plus `command`** (PowerShell für den
  .NET-Alltag, z. B. `dotnet build|test|watch`). Da diese KI-Befehle **nicht vorab geprüft** sind,
  laufen sie nur nach **Bestätigung im sichtbaren Fenster** (Befehl + Verzeichnis werden angezeigt,
  nur `j`/`y` führt aus) und nur bei `AllowCommands`. Ungültiges wird verworfen.
- **`command`/PowerShell-Buttons:** vom Nutzer bewusst freigeschaltetes Feature. Schutzschienen:
  **Bestätigung bei Erstellung** (Review-Dialog vor dem Speichern), Ausführung **immer im
  sichtbaren PowerShell-Fenster**, nur bei `press`, Warn-Style (rot/„⚠ PS"), globaler Schalter
  `AllowCommands` in `Program.cs`.

## Abgrenzung (Boundaries) — verbindlich

1. **Lokale Konfiguration bleibt lokal.** Die echten `config/*.json`, `steering.txt`,
   `buttons.json` und exportierte Deck-Profile sind via `.gitignore` ausgeschlossen — ins Repo
   gehören nur Code, Skripte und die `*.example.json`-Vorlagen. Keine Server-/Firmennamen im Code.
2. **Das Arbeits-Repo wird nicht verändert/integriert.** Einzige Verbindung: AIStreamDeck führt **von
   außen** `git`-Kommandos aus — read-only (Status) **und** schreibend nur über die dedizierten
   Tasten **Commit/Pull/Push** (bewusst freigeschaltet, nie `--force`). `ANTHROPIC_API_KEY` nur
   aus der Umgebungsvariable, wird nie geloggt oder gespeichert.

## Verhältnis zum übrigen Repo

Die `.cmd`/`.ps1`-Skripte unter `..\scripts` und das statische Profil-Konzept bleiben als
wartungsarmer Basis-Workflow bestehen (siehe [Wurzel-README](../README.md)). AIStreamDeck ist die
**Phase-2-Ausbaustufe** für Tasten mit echtem Live-Inhalt.
