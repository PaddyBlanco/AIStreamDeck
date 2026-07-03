# Plan: `IDialSurface` — Regler/LCD hinter ein eigenes Interface

**Status:** geplant, Ausführung **nach** dem grünen +-Gerätetest (sonst Regressionen nicht zuordenbar).
**Ziel:** Die +-only-Hardware (4 Drehregler + Touch-LCD) hinter eine eigene, optionale Schnittstelle
ziehen, statt in `Program.cs` direkt gegen die konkrete `StreamDeckPlus`-Klasse (`plusDev`) zu arbeiten.
Passt zum bestehenden Muster `IDeckHardware` → `SharpHardware`/`PlusHardware`.

## Warum
- `Program.cs` hängt an mehreren Stellen direkt an `plusDev` (konkreter +-Treiber): `SetTouchStrip`,
  `DialRotated`, `DialPushed`. Das ist die einzige Stelle, an der die Geräte-Abstraktion „leckt".
- Ein optionales Fähigkeiten-Interface hält das MK.2 sauber (hat keine Regler → `null`) und macht
  ein künftiges drittes Gerät mit Reglern trivial anschließbar.

## Interface (neu in `DeckHardware.cs`)
```csharp
internal interface IDialSurface        // nur Geräte mit Reglern/Touch-LCD (Stream Deck +)
{
    int DialCount { get; }             // 4
    int StripWidth { get; }            // 800
    int StripHeight { get; }           // 100
    void SetTouchStrip(Bitmap bmp);
    event Action<int, int>? DialRotated;   // (dial 0..3, delta +/-)
    event Action<int, bool>? DialPushed;   // (dial 0..3, down)
    event Action<int, int, int>? Touched;  // (type, x, y)
}
```
- `IDeckHardware` bekommt: `IDialSurface? Dials { get; }`.
- `SharpHardware.Dials => null`.
- `PlusHardware` implementiert `IDialSurface` (leitet an das gekapselte `StreamDeckPlus` weiter);
  `Dials => this`. Die `Device`-Property (Leak) entfällt.

## Änderungen in `Program.cs` (verhaltensgleich)
- `StreamDeckPlus? plusDev` → entfällt; stattdessen `IDialSurface? dials = hw.Dials;`.
- `plusDev is not null` → `dials is not null`; `plusDev.SetTouchStrip(...)` → `dials.SetTouchStrip(...)`;
  `plusDev.DialRotated/DialPushed +=` → `dials.DialRotated/DialPushed +=`.
- **Bleibt bewusst App-Logik in `Program.cs`** (nicht ins Interface): was jeder Regler tut
  (Lautstärke/Helligkeit/KI-Skill/Blättern) und was der LCD-Streifen zeigt (`UpdateStrip`).
  Das Interface abstrahiert nur die Hardware-I/O, nicht die Belegung.

## Abgrenzung / Nicht-Ziele
- **Kein** Umbau des Seiten-Modells / der `isPlus`-Layout-Zweige in diesem Schritt (separater,
  größerer Refactor — erst wenn das hier grün ist).
- `StreamDeckPlus.cs` (nativer HID-Treiber) bleibt unverändert; nur `PlusHardware` wächst um die
  Interface-Weiterleitung.

## Verifikation
- `dotnet build` grün.
- Am + : Regler 1–4 (Lautstärke/Helligkeit/KI-Skill/Blättern), LCD-Segmente und Touch identisch
  wie vorher (reine Extraktion, kein Verhaltensunterschied).
