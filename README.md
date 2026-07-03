# AIStreamDeck

Eine lokale .NET-Anwendung, die ein Elgato Stream Deck direkt über **USB-HID** ansteuert und
jede Taste zur Laufzeit selbst rendert. Eine KI beobachtet den Arbeitskontext — aktives Fenster,
Projektstruktur, Git-Zustand — und belegt das Deck fortlaufend mit den Aktionen, die in diesem
Moment tatsächlich nützlich sind. Fenster gewechselt, Tasten gewechselt.

Leitprinzip ist der **Verleitungstest**: Eine Taste rechtfertigt ihren Platz nur, wenn man dafür
freiwillig die Hände von der Tastatur nimmt. `Strg+S` scheitert an diesem Test zuverlässig.
Ein Druck, der den belegten Port freiräumt, die Tests startet und das Ergebnis öffnet, während
man weitertippt — der besteht ihn.

> Entstanden als Wochenend-Projekt, im täglichen Einsatz erwachsen geworden.
> Gebaut für Windows und einen .NET-/C#-Entwickleralltag.

## Funktionen

- **Adaptive KI-Tasten** — bei jedem Fokuswechsel schlägt die KI bis zu 9 Tasten vor, abgeleitet
  aus Programm, Fenstertitel, Projektstruktur und Git-Zustand. Unter jedem Label läuft eine
  Kurzbeschreibung als Lauftext, damit klar ist, was passiert, *bevor* man drückt.
- **Git-Cockpit** — Live-Status (Branch, dirty, ahead/behind) auf einer Taste; daneben
  **commit + pull + push in einem Druck**. Die Commit-Message erzeugt die KI aus dem staged Diff;
  bei Konflikten öffnet sich TortoiseGit zur manuellen Auflösung.
- **„Neuer Button"** — Wunsch als Freitext eingeben („kopiere mir eine GUID"), die KI erzeugt die
  Tasten-Definition, ein Review-Dialog bestätigt sie, danach liegt sie dauerhaft auf dem Deck.
- **KI-Werkzeuge** — Regex- und T-SQL-Generator in die Zwischenablage, „Erklär die Markierung",
  freie KI-Anfrage. Ohne Browser-Tab, ohne Kontextwechsel.
- **Stream Deck + Unterstützung** — 4 Drehregler und Touch-LCD über einen eigenen HID-Treiber.
  Ein Regler wird von der KI je nach aktiver Anwendung belegt (Editor: Schriftgröße,
  Browser: Zoom), Druck setzt zurück.
- **Ladeanimation** — während die KI nachdenkt, läuft ein Dino über die Tasten. Technisch
  entbehrlich, aus Gründen der Moral beibehalten.

## Hardware

| Gerät | Tasten | Extras | Anbindung |
|---|---|---|---|
| Stream Deck MK.2 / XL | 15 / 32 | — | [`StreamDeckSharp`](https://github.com/OpenMacroBoard/StreamDeckSharp) |
| Stream Deck + | 8 | 4 Drehregler, Touch-LCD | eigener HID-Treiber ([`Devices/StreamDeckPlus.cs`](src/Devices/StreamDeckPlus.cs)) |

## Schnellstart

1. **Voraussetzungen:** Windows, .NET-10-SDK, ein Stream Deck sowie ein KI-Zugang —
   Claude-Code-Abo (`claude login`), ChatGPT-Abo (`codex login`) oder ein Anthropic-API-Key.
2. **Konfiguration anlegen** (die echten Dateien bleiben per `.gitignore` lokal):
   ```
   copy config\projects.example.json config\projects.json   # eigene Projekte eintragen
   copy config\sql.example.json      config\sql.json        # optional, nur für die SQL-Skripte
   ```
3. **Elgato-Software beenden.** Die Anwendung übernimmt das Deck exklusiv per HID;
   nach dem Beenden (Strg+C) ist das Gerät wieder frei.
4. **Starten:**
   ```
   dotnet run --project src            # Hauptprogramm
   dotnet run --project src -- probe   # Selbsttest ohne Deck: Git-, Fokus- und Backend-Erkennung
   ```
5. Über die **Prompt-Taste** lässt sich der KI eine dauerhafte Vorgabe mitgeben
   (z. B. „nur Docker-Aktionen vorschlagen").

## KI-Backends

Auswahl über die Umgebungsvariable `AISTREAMDECK_AI`:

| Wert | Backend | Kosten |
|---|---|---|
| `claude` *(Default)* | Claude Code CLI über das Abo | keine API-Kosten — der API-Key wird dem Kindprozess entzogen, damit sicher das Abo abrechnet |
| `codex` | Codex CLI über das ChatGPT-Abo | keine API-Kosten, eigener Kontingent-Pool |
| `anthropic` | Anthropic-API direkt | verbraucht API-Guthaben |

**Übermittelte Daten:** Prozessname, Fenstertitel, ggf. Browser-URL, eine kompakte
Strukturbeschreibung des offenen Editor-Projekts (nur Datei- und Ordnernamen samt Pfaden) sowie
beim Commit der gekürzte staged Diff. **Keine Dateiinhalte, keine Screenshots.**
Details: [src/README.md](src/README.md).

## Architektur

```mermaid
flowchart TD
    FW["ForegroundWatcher — Fenster-Fokuswechsel"] --> CTX["WorkContext<br/>Prozess · Fenstertitel · Browser-URL<br/>Projektstruktur via WorkspaceProbe · Git-Zustand"]
    CFG[("config/<br/>projects.json · steering.txt · buttons.json")] -.-> CTX
    CTX --> FAB{"AiFactory<br/>AISTREAMDECK_AI"}
    FAB -->|"claude (Default, Abo)"| B1["ClaudeCodeBackend"]
    FAB -->|"codex (ChatGPT-Abo)"| B2["CodexBackend"]
    FAB -->|"anthropic (API)"| B3["SuggestionClient"]
    B1 --> ACT
    B2 --> ACT
    B3 --> ACT
    ACT["DeckActions — typisiert &amp; validiert (AiParse)<br/>openUrl · hotkey · focusWindow · command"] --> DC["DeckController + Visuals<br/>Icons · Farben · Lauftext · 🦖"]
    ENG["ButtonEngine — statische Seiten<br/>Git-Cockpit (GitStatus / GitOps)"] --> DC
    DC --> HW{"IDeckHardware"}
    HW -->|"MK.2 / XL"| SH["SharpHardware<br/>(StreamDeckSharp)"]
    HW -->|"Stream Deck +"| PH["PlusHardware<br/>(eigener HID-Treiber)"]
    SH --> DECK[["Stream Deck"]]
    PH --> DECK
    DECK -->|Tastendruck| EXE["ActionExecutor / Shell<br/>PowerShell nur sichtbar + j/N-Bestätigung"]
```

Der Ablauf in einem Satz: Fokuswechsel → Kontext einsammeln → KI-Backend nach Wahl →
typisierte, validierte Aktionen → Tasten rendern; jeder Tastendruck läuft durch dieselbe
Sicherheitsschleuse zurück.

## Sicherheitskonzept

- Die KI liefert ausschließlich **typisierte Aktionen** (`openUrl`, `hotkey`, `focusWindow`,
  `command`); alles außerhalb des Schemas wird verworfen.
- KI-generierte PowerShell-Befehle laufen **nie unbeaufsichtigt**: Ausführung nur in einem
  sichtbaren Fenster, das Befehl und Arbeitsverzeichnis anzeigt und eine `j/N`-Bestätigung
  verlangt (Default: Abbruch). Global abschaltbar über `AllowCommands`.
- **Keine Secrets im Repository:** SQL ausschließlich über Windows-Authentifizierung, der
  API-Key nur aus der Umgebungsvariable, echte Konfigurationsdateien sind gitignored.
- Git-Operationen auf das Arbeits-Repository erfolgen nur von außen (`git -C`), niemals
  mit `--force`.

## Betrieb ohne App: die Skript-Variante

Wer das Deck nicht exklusiv übernehmen möchte, nutzt [`scripts/`](scripts/): wartungsarme
`.cmd`/`.ps1`-Helfer (Dev-Umgebung starten, Build/Test, Ports freiräumen, SQL-Statuschecks per
Windows-Auth), die in der Elgato-Software auf „System → Öffnen"-Tasten gelegt werden. Keine
Plugins, keine Marktplatz-Abhängigkeiten; Logik ändern heißt Skript editieren. Destruktive
Skripte fragen vor der Ausführung nach. In Kombination mit den Auto-Switch-Profilen der
Elgato-Software lässt sich damit bereits viel abdecken.

## Projektstruktur

```
AIStreamDeck/
├─ src/                die Anwendung (C#, .NET 10, ein Binary)
│  ├─ Devices/         IDeckHardware-Abstraktion + nativer Stream-Deck-+-Treiber
│  ├─ Rendering/       Tasten zeichnen: Icons, Farben, Lauftext, Dino
│  ├─ Buttons/         konfigurierbare Button-Engine (buttons.json)
│  ├─ Ai/              KI-Backends (Claude/Codex/API) + der gemeinsame Prompt
│  ├─ Git/             Status lesen, commit+pull+push
│  └─ Platform/        Win32: Fokus-Watcher, Browser-URL, Clipboard, PowerShell, Dialoge
├─ scripts/            Skript-Variante für die Elgato-Software (lib/_common.ps1 = Helfer)
├─ config/             lokale Einstellungen — im Repository nur *.example.json
├─ profiles/           exportierte .streamDeckProfile-Backups (lokal)
├─ docs/               Pläne und Notizen
└─ AIStreamDeck.slnx
```

## Versionierung

- **SemVer**, Tags im Format `vMAJOR.MINOR.PATCH` — aktuell **v1.0.0**.
- `main` ist immer lauffähig (baut warnungsfrei, `probe` grün); Experimente leben in Branches.
- Breaking Changes (etwa umbenannte Config-Felder oder Umgebungsvariablen) bedeuten einen
  Major-Sprung und werden in den Release Notes hervorgehoben.
- Änderungshistorie: Commits bzw. GitHub-Releases. Ein separates CHANGELOG folgt, sobald es
  jemand ernsthaft vermisst.

## FAQ

**Warum findet die Elgato-Software das Deck nicht mehr?**
Solange AIStreamDeck läuft, hält es das Gerät exklusiv (HID). Anwendung beenden (Strg+C),
dann ist das Deck wieder frei — beide Ansätze koexistieren problemlos.

**Verbraucht das mein Claude-/ChatGPT-Kontingent?**
Ja, das Abo-Kontingent — pro Fensterwechsel höchstens ein Aufruf, danach greift ein Cache.
Wer sein Claude-Limit für die eigentliche Arbeit braucht, weicht mit `AISTREAMDECK_AI=codex`
auf den ChatGPT-Pool aus.

**Läuft das auch unter macOS/Linux?**
Nein. Die Anwendung ist bewusst Windows-nativ (Win32-Fokus-Erkennung, GDI+-Rendering,
PowerShell-Integration).

**Muss ich den Dino ernst nehmen?**
Der Dino nimmt dich auch nicht ernst.
