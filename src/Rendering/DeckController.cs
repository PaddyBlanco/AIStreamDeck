using System.Drawing;
using System.Drawing.Imaging;

namespace AIStreamDeck;

/// <summary>Zeichnet Text/Farbe/Icons auf Tasten — geraeteunabhaengig ueber IDeckHardware.</summary>
internal sealed class DeckController(IDeckHardware hw)
{
    public int CountX => hw.CountX;
    public int CountY => hw.CountY;
    private int Size => hw.KeySize;

    public int KeyId(int col, int row) => row * CountX + col;

    private static string? _iconFamily;

    /// <summary>Monochrome Icon-Schrift (Win 11: Segoe Fluent Icons, sonst MDL2). Null = keine.</summary>
    private static string? IconFamily()
    {
        if (_iconFamily is not null) return _iconFamily.Length == 0 ? null : _iconFamily;
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try { using var ff = new FontFamily(name); _iconFamily = name; return name; }
            catch { /* nicht installiert */ }
        }
        _iconFamily = "";
        return null;
    }

    /// <summary>Animierte Ruhe-Darstellung: Gradient + Icon + Label + Unterzeile + Akzentbalken.
    /// tick treibt den Lauftext, pulse (0..1) die Akzent-Helligkeit.</summary>
    public void DrawKey(int keyId, KeyVisual v, int tick, float pulse)
    {
        int size = Size;
        bool hasIcon = !string.IsNullOrEmpty(v.Glyph);
        bool hasSub = !string.IsNullOrWhiteSpace(v.Sub);
        int accentH = Math.Max(3, size / 18);

        using var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            var rect = new Rectangle(0, 0, size, size);
            using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
                rect, ColorFx.Mix(v.Color, Color.Black, 0.80f), ColorFx.Mix(v.Color, Color.Black, 0.55f),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                g.FillRectangle(bg, rect);

            using (var accent = new SolidBrush(ColorFx.Scale(v.Color, 0.55f + 0.45f * pulse)))
                g.FillRectangle(accent, 0, size - accentH, size, accentH);

            if (v.Ki) // roter Rand unten -> markiert die adaptiven KI-Tasten (Seite 1 auf dem +)
            {
                int redH = Math.Max(4, size / 14);
                using var red = new SolidBrush(Color.FromArgb(225, 45, 45));
                g.FillRectangle(red, 0, size - redH, size, redH);
            }

            float iconH = hasIcon ? size * 0.40f : 0f;
            if (hasIcon)
            {
                float ib = size * 0.32f;
                IconDraw.Draw(g, v.Glyph!, new RectangleF((size - ib) / 2f, size * 0.05f, ib, ib), Color.White);
            }

            float subH = hasSub ? size * 0.24f : 0f;
            float labelTop = hasIcon ? iconH : 2f;
            float labelH = size - labelTop - subH - accentH;
            using var labelFont = LabelFont(size);
            DrawTextMaybeScroll(g, v.Label, labelFont, Brushes.White,
                new RectangleF(2, labelTop, size - 4, labelH), tick, size);

            if (hasSub)
            {
                using var subFont = SubFont(size);
                using var subBrush = new SolidBrush(Color.FromArgb(185, 205, 220));
                DrawTextMaybeScroll(g, v.Sub!, subFont, subBrush,
                    new RectangleF(2, size - subH - accentH, size - 4, subH), tick, size);
            }
        }
        hw.SetKey(keyId, bmp);
    }

    /// <summary>True, wenn Label oder Unterzeile breiter als die Taste sind (-> Lauftext).</summary>
    public bool Overflows(KeyVisual v)
    {
        int size = Size;
        float avail = size - 4;
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        using var lf = LabelFont(size);
        if (Width(g, v.Label, lf) > avail) return true;
        if (!string.IsNullOrWhiteSpace(v.Sub))
        {
            using var sf = SubFont(size);
            if (Width(g, v.Sub!, sf) > avail) return true;
        }
        return false;
    }

    private static float Width(Graphics g, string text, Font font)
        => g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic).Width;

    private static Font LabelFont(int size) => new("Segoe UI", size > 90 ? 11f : 8.5f, FontStyle.Bold);
    private static Font SubFont(int size) => new("Segoe UI", size > 90 ? 8f : 6.5f, FontStyle.Regular);

    private static StringFormat Centered() => new(StringFormatFlags.NoWrap)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    private static void DrawTextMaybeScroll(Graphics g, string text, Font font, Brush brush,
        RectangleF area, int tick, int size)
    {
        float w = Width(g, text, font);
        if (w <= area.Width)
        {
            using var c = Centered();
            g.DrawString(text, font, brush, area, c);
            return;
        }
        float gap = area.Width * 0.5f;
        float period = w + gap;
        float speed = Math.Max(2f, size * 0.045f);
        float off = (tick * speed) % period;
        g.SetClip(area);
        using var l = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
        };
        float runW = w + gap + area.Width;
        g.DrawString(text, font, brush, new RectangleF(area.X - off, area.Y, runW, area.Height), l);
        g.DrawString(text, font, brush, new RectangleF(area.X - off + period, area.Y, runW, area.Height), l);
        g.ResetClip();
    }

    public void Draw(int keyId, string title, Color background)
    {
        int size = Size;
        using var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(background);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (!string.IsNullOrWhiteSpace(title))
            {
                using var font = new Font("Segoe UI", size > 90 ? 13f : 9f, FontStyle.Bold);
                using var fmt = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisWord,
                };
                var rect = new RectangleF(2, 2, size - 4, size - 4);
                g.DrawString(title, font, Brushes.White, rect, fmt);
            }
        }
        hw.SetKey(keyId, bmp);
    }

    /// <summary>Laufender Pixel-Dino (frame 0/1) auf buntem Hintergrund — fuer die Lade-Animation.</summary>
    public void DrawDino(int keyId, int frame, Color bg)
    {
        int size = Size;
        using var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            var rect = new Rectangle(0, 0, size, size);
            using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                rect, ColorFx.Mix(bg, Color.Black, 0.55f), ColorFx.Mix(bg, Color.Black, 0.15f),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                g.FillRectangle(grad, rect);

            int groundH = Math.Max(2, size / 22);
            using (var ground = new SolidBrush(Color.FromArgb(245, 245, 245)))
                g.FillRectangle(ground, 0, size - groundH, size, groundH);

            float bounce = frame == 0 ? 0f : -size * 0.03f; // leichtes Auf-und-Ab
            float m = size * 0.12f;
            Dino.Draw(g, frame, new RectangleF(m, m + bounce, size - 2 * m, size - 2 * m - size * 0.08f), Color.White);
        }
        hw.SetKey(keyId, bmp);
    }
}
