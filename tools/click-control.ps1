# Sends BM_CLICK to a child control of a window, found by its caption.
# Used to drive the app during verification without a full UI-automation stack.
param(
    [Parameter(Mandatory = $true)][string]$WindowTitle,
    [Parameter(Mandatory = $true)][string]$ControlText
)

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Clicker
{
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

    public delegate bool EnumProc(IntPtr h, IntPtr p);

    private const uint BM_CLICK = 0x00F5;

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

    public static bool Click(IntPtr root, string caption)
    {
        IntPtr target = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) {
            StringBuilder sb = new StringBuilder(256);
            GetWindowTextW(h, sb, 256);
            if (sb.ToString().Equals(caption, StringComparison.OrdinalIgnoreCase)) { target = h; return false; }
            return true;
        }, IntPtr.Zero);

        if (target == IntPtr.Zero) return false;

        SetForegroundWindow(root);
        SendMessageW(target, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        return true;
    }
}
'@

$hwnd = [Clicker]::Top($WindowTitle)
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "no window titled '$WindowTitle'"; exit 1 }
if (-not [Clicker]::Click($hwnd, $ControlText)) { Write-Error "no control captioned '$ControlText'"; exit 1 }
Write-Host "clicked '$ControlText'"
