# Captures a single top-level window to a PNG, using PrintWindow so the shot is correct
# even when the window is behind something else.
param(
    [Parameter(Mandatory = $true)][string]$TitleContains,
    [Parameter(Mandatory = $true)][string]$Out
)

Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

public static class WinCap
{
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);

    public delegate bool EnumProc(IntPtr hwnd, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static IntPtr Find(string needle)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, p) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            if (t.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static Bitmap Capture(IntPtr hwnd)
    {
        RECT win;
        GetWindowRect(hwnd, out win);

        int winW = win.Right - win.Left;
        int winH = win.Bottom - win.Top;
        if (winW <= 0 || winH <= 0) return null;

        // PrintWindow always draws from the window rect's origin, so render at that size...
        var full = new Bitmap(winW, winH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
        {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(hwnd, hdc, 2); }   // 2 = PW_RENDERFULLCONTENT
            finally { g.ReleaseHdc(hdc); }
        }

        // ...then crop away the invisible resize border, which the extended frame bounds
        // excludes. Without this the visible content sits several pixels off.
        RECT frame;
        if (DwmGetWindowAttribute(hwnd, 9, out frame, Marshal.SizeOf(typeof(RECT))) != 0)
        {
            return full;
        }

        var crop = new Rectangle(
            frame.Left - win.Left,
            frame.Top - win.Top,
            frame.Right - frame.Left,
            frame.Bottom - frame.Top);

        crop.Intersect(new Rectangle(0, 0, winW, winH));
        if (crop.Width <= 0 || crop.Height <= 0) return full;

        Bitmap cropped = full.Clone(crop, full.PixelFormat);
        full.Dispose();
        return cropped;
    }
}
'@ -ReferencedAssemblies System.Drawing, System.Drawing.Primitives

$hwnd = [WinCap]::Find($TitleContains)
if ($hwnd -eq [IntPtr]::Zero) {
    Write-Error "No visible window whose title contains '$TitleContains'"
    exit 1
}

$bmp = [WinCap]::Capture($hwnd)
if ($null -eq $bmp) {
    Write-Error "Window has no drawable area"
    exit 1
}

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Saved $Out  ($($bmp.Width)x$($bmp.Height))"
$bmp.Dispose()
