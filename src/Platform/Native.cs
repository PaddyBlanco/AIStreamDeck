using System.Runtime.InteropServices;
using System.Text;

namespace AIStreamDeck;

/// <summary>Win32-Interop fuer Vordergrundfenster-Erkennung, Fokus und Hotkeys.</summary>
internal static class Native
{
    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern short VkKeyScan(char ch); // LoByte = VK, HiByte = Shift-Status

    public const uint KEYEVENTF_KEYUP = 0x0002;

    public static string GetWindowTitle(nint hWnd)
    {
        var sb = new StringBuilder(512);
        int len = GetWindowTextW(hWnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }
}
