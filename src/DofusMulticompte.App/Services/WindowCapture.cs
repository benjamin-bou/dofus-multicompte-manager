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
    /// <summary>Résultat d'une capture : <c>Hung</c> est vrai si la fenêtre ne répond plus.</summary>
    public readonly record struct CaptureResult(BitmapSource? Image, bool Hung);

    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(3);

    // Captures en cours par fenêtre : une capture bloquée sur une fenêtre gelée ne doit pas en empiler d'autres.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, Task<BitmapSource?>> InFlight = new();

    /// <summary>
    /// Capture sans jamais rester bloqué : PrintWindow attend indéfiniment une fenêtre gelée.
    /// Une fenêtre signalée comme gelée par Windows, ou dont la capture dépasse 3 s, est renvoyée comme <c>Hung</c>.
    /// </summary>
    public static async Task<CaptureResult> CaptureAsync(IntPtr hwnd, int maxWidth)
    {
        if (IsHungAppWindow(hwnd)) return new(null, true);
        if (InFlight.TryGetValue(hwnd, out var pending) && !pending.IsCompleted) return new(null, true);

        var task = Task.Run(() => Capture(hwnd, maxWidth));
        InFlight[hwnd] = task;
        _ = task.ContinueWith(_ => InFlight.TryRemove(new KeyValuePair<IntPtr, Task<BitmapSource?>>(hwnd, task)), TaskScheduler.Default);

        var finished = await Task.WhenAny(task, Task.Delay(CaptureTimeout));
        return finished == task ? new(await task, false) : new(null, true);
    }

    /// <summary>Capture réduite sous forme de pixels BGR32 (4 octets par pixel), pour l'analyse d'image.</summary>
    public static async Task<(byte[] Pixels, int Width, int Height, int Stride)?> CapturePixelsAsync(IntPtr hwnd, int maxWidth, Action onHung)
    {
        var result = await CaptureAsync(hwnd, maxWidth);
        if (result.Hung) onHung();
        if (result.Image is not { } image) return null;
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        return (pixels, image.PixelWidth, image.PixelHeight, stride);
    }

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
