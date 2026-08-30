# Prints every child control of a top-level window with its client-relative bounds.
# Diagnostic aid: the source of truth when a hand-coded layout does not look right.
param([Parameter(Mandatory = $true)][string]$Title)

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Enumr
{
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);

    public delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public static IntPtr Top(string needle)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p) {
            if (!IsWindowVisible(h)) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            if (sb.ToString().Equals(needle, StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Dump(IntPtr root)
    {
        StringBuilder output = new StringBuilder();
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) {
            StringBuilder cls = new StringBuilder(256);
            GetClassNameW(h, cls, 256);
            StringBuilder txt = new StringBuilder(256);
            GetWindowTextW(h, txt, 256);

            RECT r;
            GetWindowRect(h, out r);
            POINT pt;
            pt.X = r.Left; pt.Y = r.Top;
            ScreenToClient(root, ref pt);

            output.AppendLine(string.Format("{0,-30} x={1,4} y={2,4} w={3,4} h={4,3} vis={5,-5} '{6}'",
                cls.ToString().Replace("WindowsForms10.", "").Split(new char[] { '.' })[0],
                pt.X, pt.Y, r.Right - r.Left, r.Bottom - r.Top, IsWindowVisible(h), txt.ToString()));
            return true;
        }, IntPtr.Zero);
        return output.ToString();
    }
}
'@

$hwnd = [Enumr]::Top($Title)
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "no window titled '$Title'"; exit 1 }
Write-Host "window handle $hwnd"
[Enumr]::Dump($hwnd)
