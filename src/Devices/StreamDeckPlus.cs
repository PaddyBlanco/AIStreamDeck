using System.Drawing;
using System.Drawing.Imaging;
using HidSharp;

namespace AIStreamDeck;

/// <summary>
/// Nativer Treiber fuer das Stream Deck + (StreamDeckSharp unterstuetzt es nicht).
/// 8 Tasten (120x120 JPEG ueber Output-Report 0x02), 4 Regler + Touch ueber Input 0x01.
/// Protokoll aus dem echten Geraet ausgemessen.
/// </summary>
internal sealed class StreamDeckPlus : IDisposable
{
    public const int KeyCount = 8, Cols = 4, Rows = 2, KeySize = 120;
    public const int TouchW = 800, TouchH = 100;   // LCD-Streifen ueber den Reglern
    private const int Vid = 0x0FD9, Pid = 0x0084;
    private const int ReportLen = 1024, HeaderLen = 8, PayloadMax = ReportLen - HeaderLen;

    private readonly HidStream _stream;
    private readonly Thread _reader;
    private readonly object _writeGate = new();
    private volatile bool _run = true;
    private readonly bool[] _btn = new bool[KeyCount];
    private readonly bool[] _dialDown = new bool[4];

    public event Action<int, bool>? KeyChanged;   // (keyIndex 0..7, down)
    public event Action<int, int>? DialRotated;   // (dialIndex 0..3, delta +/-)
    public event Action<int, bool>? DialPushed;   // (dialIndex 0..3, down)
    public event Action<int, int, int>? Touched;  // (type, x, y) — Touch auf dem LCD-Streifen

    public static StreamDeckPlus? TryOpen()
    {
        var dev = DeviceList.Local.GetHidDevices(Vid, Pid).FirstOrDefault();
        if (dev is null || !dev.TryOpen(out HidStream s)) return null;
        return new StreamDeckPlus(s);
    }

    private StreamDeckPlus(HidStream stream)
    {
        _stream = stream;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "SDPlus-Read" };
        _reader.Start();
    }

    public void SetBrightness(int percent)
    {
        var b = new byte[32]; b[0] = 0x03; b[1] = 0x08; b[2] = (byte)Math.Clamp(percent, 0, 100);
        lock (_writeGate) { try { _stream.SetFeature(b); } catch { } }
    }

    public void Reset()
    {
        var b = new byte[32]; b[0] = 0x03; b[1] = 0x02;
        lock (_writeGate) { try { _stream.SetFeature(b); } catch { } }
    }

    public void SetKeyBitmap(int key, Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        SetKeyImage(key, ms.ToArray());
    }

    /// <summary>Setzt das komplette LCD-Streifen-Bild (800x100).</summary>
    public void SetTouchStrip(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        var jpeg = ms.ToArray();
        const int header = 16; // LCD-Report-Header ist 16 Bytes (Felder bei 2/4/6/8/10/11, Rest Padding)
        int payloadMax = ReportLen - header;
        int page = 0, sent = 0;
        lock (_writeGate)
        {
            while (true)
            {
                int len = Math.Min(payloadMax, jpeg.Length - sent);
                bool last = sent + len >= jpeg.Length;
                var pkt = new byte[ReportLen];
                pkt[0] = 0x02; pkt[1] = 0x0C;
                pkt[2] = 0; pkt[3] = 0;                                  // x = 0
                pkt[4] = 0; pkt[5] = 0;                                  // y = 0
                pkt[6] = TouchW & 0xFF; pkt[7] = (byte)(TouchW >> 8);   // w = 800
                pkt[8] = TouchH & 0xFF; pkt[9] = (byte)(TouchH >> 8);   // h = 100
                pkt[10] = (byte)(last ? 1 : 0);
                pkt[11] = (byte)(page & 0xFF); pkt[12] = (byte)(page >> 8);
                pkt[13] = (byte)(len & 0xFF); pkt[14] = (byte)(len >> 8); // Laenge dieses Pakets
                pkt[15] = 0x00;
                if (len > 0) Array.Copy(jpeg, sent, pkt, header, len);
                try { _stream.Write(pkt); } catch { break; }
                sent += len; page++;
                if (last) break;
            }
        }
    }

    private void SetKeyImage(int key, byte[] jpeg)
    {
        int page = 0, sent = 0;
        lock (_writeGate)
        {
            while (true)
            {
                int len = Math.Min(PayloadMax, jpeg.Length - sent);
                bool last = sent + len >= jpeg.Length;
                var pkt = new byte[ReportLen];
                pkt[0] = 0x02; pkt[1] = 0x07; pkt[2] = (byte)key; pkt[3] = (byte)(last ? 1 : 0);
                pkt[4] = (byte)(len & 0xFF); pkt[5] = (byte)(len >> 8);
                pkt[6] = (byte)(page & 0xFF); pkt[7] = (byte)(page >> 8);
                if (len > 0) Array.Copy(jpeg, sent, pkt, HeaderLen, len);
                try { _stream.Write(pkt); } catch { break; }
                sent += len; page++;
                if (last) break;
            }
        }
    }

    private void ReadLoop()
    {
        _stream.ReadTimeout = 200;
        var buf = new byte[512];
        while (_run)
        {
            int n;
            try { n = _stream.Read(buf); }
            catch (TimeoutException) { continue; }
            catch { break; }
            if (n < 5 || buf[0] != 0x01) continue;

            switch (buf[1])
            {
                case 0x00: // Tasten
                    for (int i = 0; i < KeyCount && 4 + i < n; i++)
                    {
                        bool down = buf[4 + i] != 0;
                        if (down != _btn[i]) { _btn[i] = down; KeyChanged?.Invoke(i, down); }
                    }
                    break;
                case 0x03: // Regler
                    if (buf[4] == 0x01) // drehen
                    {
                        for (int i = 0; i < 4 && 5 + i < n; i++)
                        {
                            sbyte d = (sbyte)buf[5 + i];
                            if (d != 0) DialRotated?.Invoke(i, d);
                        }
                    }
                    else if (buf[4] == 0x00) // druecken (Zustand)
                    {
                        for (int i = 0; i < 4 && 5 + i < n; i++)
                        {
                            bool down = buf[5 + i] != 0;
                            if (down != _dialDown[i]) { _dialDown[i] = down; DialPushed?.Invoke(i, down); }
                        }
                    }
                    break;
                case 0x02: // Touchscreen
                    if (n >= 10)
                    {
                        int type = buf[4];                       // 1=kurz, 2=lang, 3=wischen
                        int x = buf[6] | (buf[7] << 8);
                        int y = buf[8] | (buf[9] << 8);
                        Touched?.Invoke(type, x, y);
                    }
                    break;
            }
        }
    }

    public void Dispose()
    {
        _run = false;
        try { _reader.Join(600); } catch { }
        try { _stream.Dispose(); } catch { }
    }
}
