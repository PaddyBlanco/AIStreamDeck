using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenMacroBoard.SDK;

namespace AIStreamDeck;

/// <summary>Geraeteunabhaengige Deck-Schnittstelle (MK.2/XL via StreamDeckSharp, + via nativem Treiber).</summary>
internal interface IDeckHardware : IDisposable
{
    int CountX { get; }
    int CountY { get; }
    int KeySize { get; }
    bool IsPlus { get; }
    void SetKey(int keyId, Bitmap bmp);
    void Clear();
    void SetBrightness(int percent);
    event Action<int, bool>? KeyChanged; // (keyId, down)
}

/// <summary>Adapter fuer StreamDeckSharp-Decks (Original/MK.2/XL/Mini).</summary>
internal sealed class SharpHardware : IDeckHardware
{
    private readonly IMacroBoard _board;
    private readonly object _gate = new();

    public SharpHardware(IMacroBoard board)
    {
        _board = board;
        _board.KeyStateChanged += (_, e) => KeyChanged?.Invoke(e.Key, e.IsDown);
    }

    public int CountX => _board.Keys.CountX;
    public int CountY => _board.Keys.CountY;
    public int KeySize => _board.Keys.KeySize;
    public bool IsPlus => false;
    public event Action<int, bool>? KeyChanged;

    public void SetBrightness(int percent) => _board.SetBrightness((byte)Math.Clamp(percent, 0, 100));
    public void Clear() { try { _board.ClearKeys(); } catch { } }

    public void SetKey(int keyId, Bitmap bmp)
    {
        int size = KeySize;
        var data = ToBgr24(bmp, size);
        lock (_gate) _board.SetKeyBitmap(keyId, KeyBitmap.Create.FromBgr24Array(size, size, data));
    }

    public void Dispose() { try { _board.Dispose(); } catch { } }

    private static byte[] ToBgr24(Bitmap bmp, int size)
    {
        Bitmap src = bmp;
        bool temp = false;
        if (bmp.Width != size || bmp.Height != size) { src = new Bitmap(bmp, size, size); temp = true; }
        var locked = src.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int stride = locked.Stride;
            var full = new byte[stride * size];
            Marshal.Copy(locked.Scan0, full, 0, full.Length);
            var outArr = new byte[size * size * 3];
            for (int y = 0; y < size; y++) Array.Copy(full, y * stride, outArr, y * size * 3, size * 3);
            return outArr;
        }
        finally { src.UnlockBits(locked); if (temp) src.Dispose(); }
    }
}

/// <summary>Adapter fuer das Stream Deck + (nativer Treiber). Gibt zusaetzlich Regler/Touch/LCD frei.</summary>
internal sealed class PlusHardware : IDeckHardware
{
    private readonly StreamDeckPlus _plus;

    public PlusHardware(StreamDeckPlus plus)
    {
        _plus = plus;
        _plus.KeyChanged += (k, d) => KeyChanged?.Invoke(k, d);
    }

    public StreamDeckPlus Device => _plus;
    public int CountX => StreamDeckPlus.Cols;
    public int CountY => StreamDeckPlus.Rows;
    public int KeySize => StreamDeckPlus.KeySize;
    public bool IsPlus => true;
    public event Action<int, bool>? KeyChanged;

    public void SetBrightness(int percent) => _plus.SetBrightness(percent);

    public void Clear()
    {
        using var b = new Bitmap(KeySize, KeySize);
        using (var g = Graphics.FromImage(b)) g.Clear(Color.Black);
        for (int k = 0; k < StreamDeckPlus.KeyCount; k++) _plus.SetKeyBitmap(k, b);
    }

    public void SetKey(int keyId, Bitmap bmp)
    {
        if (bmp.Width == KeySize && bmp.Height == KeySize) { _plus.SetKeyBitmap(keyId, bmp); return; }
        using var scaled = new Bitmap(bmp, KeySize, KeySize);
        _plus.SetKeyBitmap(keyId, scaled);
    }

    public void Dispose() { try { _plus.Reset(); } catch { } _plus.Dispose(); }
}
