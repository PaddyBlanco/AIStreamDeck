using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AIStreamDeck;

/// <summary>Selbst gezeichnete Vektor-Icons (GDI) — rendern immer, keine Icon-Schrift noetig.</summary>
internal static class IconDraw
{
    public static void Draw(Graphics g, string name, RectangleF box, Color color)
    {
        float s = Math.Min(box.Width, box.Height);
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        var r = new RectangleF(cx - s / 2f, cy - s / 2f, s, s);
        float pw = Math.Max(2f, s * 0.11f);
        using var p = new Pen(color, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var b = new SolidBrush(color);

        switch (name)
        {
            case "check":
                g.DrawLines(p, new[] { P(r, .16f, .55f), P(r, .42f, .78f), P(r, .84f, .24f) });
                break;
            case "up":
            case "down":
                bool up = name == "up";
                float tip = up ? .15f : .85f, baseY = up ? .52f : .48f, stem = up ? .85f : .15f;
                g.FillPolygon(b, new[] { P(r, .5f, tip), P(r, .20f, baseY), P(r, .80f, baseY) });
                g.DrawLine(p, P(r, .5f, baseY), P(r, .5f, stem));
                break;
            case "play":
                g.FillPolygon(b, new[] { P(r, .30f, .16f), P(r, .30f, .84f), P(r, .84f, .5f) });
                break;
            case "chat":
                using (var path = Rounded(R(r, .12f, .16f, .76f, .52f), s * .12f)) g.FillPath(b, path);
                g.FillPolygon(b, new[] { P(r, .30f, .64f), P(r, .26f, .86f), P(r, .48f, .66f) });
                break;
            case "globe":
                g.DrawEllipse(p, R(r, .14f, .14f, .72f, .72f));
                g.DrawEllipse(p, R(r, .37f, .14f, .26f, .72f));
                g.DrawLine(p, P(r, .14f, .5f), P(r, .86f, .5f));
                break;
            case "keyboard":
                using (var path = Rounded(R(r, .10f, .28f, .80f, .44f), s * .08f)) g.DrawPath(p, path);
                for (int i = 0; i < 3; i++) g.FillEllipse(b, Dot(r, .28f + i * .22f, .42f, s * .055f));
                g.FillRectangle(b, R(r, .30f, .56f, .40f, .07f));
                break;
            case "window":
                g.DrawRectangle(p, RectI(R(r, .14f, .18f, .72f, .64f)));
                g.DrawLine(p, P(r, .14f, .34f), P(r, .86f, .34f));
                break;
            case "star":
                g.FillPolygon(b, Star(cx, cy, s * .46f, s * .19f));
                break;
            case "edit": // Stift
                g.DrawLine(p, P(r, .30f, .72f), P(r, .76f, .26f));
                g.FillPolygon(b, new[] { P(r, .16f, .86f), P(r, .32f, .70f), P(r, .22f, .62f) });
                break;
            case "sync": // zwei Pfeile im Kreis (vereinfacht)
                g.DrawArc(p, R(r, .18f, .18f, .64f, .64f), 30, 250);
                g.FillPolygon(b, new[] { P(r, .80f, .18f), P(r, .86f, .42f), P(r, .64f, .34f) });
                break;
            case "rocket":
                g.FillPolygon(b, new[] { P(r, .5f, .12f), P(r, .66f, .55f), P(r, .5f, .70f), P(r, .34f, .55f) });
                g.FillPolygon(b, new[] { P(r, .34f, .55f), P(r, .24f, .80f), P(r, .42f, .66f) });
                g.FillPolygon(b, new[] { P(r, .66f, .55f), P(r, .76f, .80f), P(r, .58f, .66f) });
                break;
        }
    }

    private static PointF P(RectangleF r, float fx, float fy) => new(r.X + fx * r.Width, r.Y + fy * r.Height);
    private static RectangleF R(RectangleF r, float fx, float fy, float fw, float fh)
        => new(r.X + fx * r.Width, r.Y + fy * r.Height, fw * r.Width, fh * r.Height);
    private static Rectangle RectI(RectangleF r) => Rectangle.Round(r);
    private static RectangleF Dot(RectangleF r, float fx, float fy, float rad)
    { var c = P(r, fx, fy); return new(c.X - rad, c.Y - rad, rad * 2, rad * 2); }
    private static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var gp = new GraphicsPath(); float d = rad * 2;
        gp.AddArc(r.X, r.Y, d, d, 180, 90);
        gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        gp.CloseFigure(); return gp;
    }
    private static PointF[] Star(float cx, float cy, float outer, float inner)
    {
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double ang = -Math.PI / 2 + i * Math.PI / 5;
            float rad = (i % 2 == 0) ? outer : inner;
            pts[i] = new((float)(cx + rad * Math.Cos(ang)), (float)(cy + rad * Math.Sin(ang)));
        }
        return pts;
    }
}

/// <summary>Was auf einer Taste steht: Icon, Label, Unterzeile, Akzentfarbe, optional Puls.
/// Ki=true zeichnet zusaetzlich einen roten Rand unten (Kennzeichnung der adaptiven KI-Tasten).</summary>
internal readonly record struct KeyVisual(string? Glyph, string Label, string? Sub, Color Color, bool Pulse = false, bool Ki = false);

/// <summary>Pixel-T-Rex (Chrome-Dino-Stil) mit 2 Bein-Stellungen fuer die Lauf-Animation.</summary>
internal static class Dino
{
    private static readonly string[] Body =
    {
        "............####",
        "............####",
        "............#.##",
        "............####",
        "#...........###.",
        "##.........####.",
        "##........#####.",
        "###......######.",
        "####....#######.",
        ".#############..",
        "..############..",
        "..###########...",
        "..##########....",
        "..###....###....",
    };
    private static readonly string[] LegsA = { "..##......##....", "..#.......#....." };
    private static readonly string[] LegsB = { "...##....#......", "...#.....##....." };

    public static void Draw(Graphics g, int frame, RectangleF box, Color color)
    {
        var legs = frame == 0 ? LegsA : LegsB;
        const int cols = 16;
        int rows = Body.Length + legs.Length;
        float cell = Math.Min(box.Width / cols, box.Height / rows);
        float ox = box.X + (box.Width - cell * cols) / 2f;
        float oy = box.Y + (box.Height - cell * rows) / 2f;
        using var b = new SolidBrush(color);

        void Row(string line, int r)
        {
            for (int c = 0; c < line.Length && c < cols; c++)
                if (line[c] == '#')
                    g.FillRectangle(b, ox + c * cell, oy + r * cell, cell + 0.7f, cell + 0.7f);
        }
        for (int r = 0; r < Body.Length; r++) Row(Body[r], r);
        for (int r = 0; r < legs.Length; r++) Row(legs[r], Body.Length + r);
    }
}

/// <summary>Kraeftige Farbpalette pro Kategorie („happy + Dev").</summary>
internal static class Palette
{
    public static readonly Color GitClean = Color.FromArgb(40, 175, 90);
    public static readonly Color GitDirty = Color.FromArgb(215, 150, 30);
    public static readonly Color GitError = Color.FromArgb(205, 60, 60);
    public static readonly Color Commit = Color.FromArgb(30, 180, 160);
    public static readonly Color Pull = Color.FromArgb(60, 120, 225);
    public static readonly Color Push = Color.FromArgb(150, 90, 225);
    public static readonly Color DevUp = Color.FromArgb(80, 200, 70);
    public static readonly Color Claude = Color.FromArgb(230, 140, 50);
    public static readonly Color Loading = Color.FromArgb(215, 150, 30);
    public static readonly Color Idle = Color.FromArgb(70, 80, 110);

    public static Color ForType(string type) => type switch
    {
        "openUrl" => Color.FromArgb(40, 185, 205),    // cyan
        "hotkey" => Color.FromArgb(165, 95, 225),     // lila
        "focusWindow" => Color.FromArgb(230, 150, 55),// orange
        "command" => Color.FromArgb(190, 70, 45),     // warn-rot (KI-PowerShell)
        _ => Idle,
    };
}

internal static class ColorFx
{
    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static Color Scale(Color c, float f)
        => Color.FromArgb(Clamp(c.R * f), Clamp(c.G * f), Clamp(c.B * f));

    /// <summary>Vollgesaettigte Regenbogenfarbe fuer Hue 0..360 (Celebrate-Welle).</summary>
    public static Color Hsv(double h)
    {
        double x = 1.0 - Math.Abs((h / 60.0) % 2 - 1);
        (double r, double g, double b) = h switch
        {
            < 60 => (1.0, x, 0.0),
            < 120 => (x, 1.0, 0.0),
            < 180 => (0.0, 1.0, x),
            < 240 => (0.0, x, 1.0),
            < 300 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x),
        };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    private static int Clamp(float v) => (int)Math.Clamp(v, 0f, 255f);
}

/// <summary>
/// Ein Timer (~10 fps) zeichnet nur die Tasten neu, die wirklich animieren
/// (Puls oder Lauftext bei Ueberlauf). Statische Tasten werden einmalig gezeichnet.
/// </summary>
internal sealed class KeyAnimator : IDisposable
{
    private readonly DeckController _deck;
    private readonly Timer _timer;
    private readonly ConcurrentDictionary<int, KeyVisual> _animated = new();
    private int _tick;

    public KeyAnimator(DeckController deck)
    {
        _deck = deck;
        _timer = new Timer(_ => Tick(), null, 100, 100);
    }

    /// <summary>Setzt die Ruhe-Darstellung einer Taste. Animiert nur bei Bedarf.</summary>
    public void Set(int keyId, KeyVisual v)
    {
        if (v.Pulse || _deck.Overflows(v))
        {
            _animated[keyId] = v;
            _deck.DrawKey(keyId, v, _tick, 1f);
        }
        else
        {
            _animated.TryRemove(keyId, out _);
            _deck.DrawKey(keyId, v, 0, 1f);
        }
    }

    /// <summary>Taste aus der Animation nehmen (z. B. waehrend Lade-/Feedback-Zustand).</summary>
    public void Release(int keyId) => _animated.TryRemove(keyId, out _);

    private void Tick()
    {
        int t = Interlocked.Increment(ref _tick);
        foreach (var kv in _animated)
        {
            float pulse = kv.Value.Pulse ? 0.74f + 0.26f * (float)Math.Sin(t * 0.45) : 1f;
            try { _deck.DrawKey(kv.Key, kv.Value, t, pulse); } catch { }
        }
    }

    public void Dispose() => _timer.Dispose();
}
