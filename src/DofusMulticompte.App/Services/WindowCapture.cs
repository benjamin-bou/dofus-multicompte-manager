using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static DofusMulticompte.App.Interop.NativeMethods;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Capture une miniature d'une fenêtre, même si elle est cachée derrière d'autres
/// (PrintWindow avec PW_RENDERFULLCONTENT fonctionne avec le rendu DirectX d'Unity).
/// </summary>
public static class WindowCapture
{
    public static BitmapSource? Capture(IntPtr hwnd, int maxWidth)
    {
        if (!IsWindow(hwnd) || IsIconic(hwnd)) return null;
        if (!GetClientRect(hwnd, out var rect)) return null;
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w < 16 || h < 16) return null;

        var tw = Math.Min(maxWidth, w);
        var th = Math.Max(1, (int)Math.Round(h * (tw / (double)w)));

        var screen = GetDC(IntPtr.Zero);
        var fullDc = CreateCompatibleDC(screen);
        var thumbDc = CreateCompatibleDC(screen);
        var fullBmp = CreateCompatibleBitmap(screen, w, h);
        var thumbBmp = CreateCompatibleBitmap(screen, tw, th);
        var oldFull = SelectObject(fullDc, fullBmp);
        var oldThumb = SelectObject(thumbDc, thumbBmp);
        try
        {
            if (!PrintWindow(hwnd, fullDc, PW_RENDERFULLCONTENT)) return null;
            SetStretchBltMode(thumbDc, HALFTONE);
            SetBrushOrgEx(thumbDc, 0, 0, IntPtr.Zero);
            StretchBlt(thumbDc, 0, 0, tw, th, fullDc, 0, 0, w, h, SRCCOPY);
            SelectObject(thumbDc, oldThumb);
            oldThumb = IntPtr.Zero;

            var raw = Imaging.CreateBitmapSourceFromHBitmap(thumbBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // PrintWindow laisse le canal alpha à 0 : sans conversion en Bgr32, WPF affiche une image transparente.
            var opaque = new FormatConvertedBitmap(raw, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return opaque;
        }
        finally
        {
            if (oldThumb != IntPtr.Zero) SelectObject(thumbDc, oldThumb);
            SelectObject(fullDc, oldFull);
            DeleteObject(fullBmp);
            DeleteObject(thumbBmp);
            DeleteDC(fullDc);
            DeleteDC(thumbDc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
