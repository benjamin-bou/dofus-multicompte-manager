using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DofusMulticompte.Core.ResourcePaths;
using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.App.Controls;

/// <summary>Ce que la superposition doit dessiner, en pixels de la zone d'image de la minimap.</summary>
public sealed record PathOverlayState(
    IReadOnlyList<MapPosition> Steps,
    int Index,
    PathStatus Status,
    MapPosition Current,
    MapPosition? Target,
    Point Marker,
    double CellWidth,
    double CellHeight,
    bool Calibrated);

/// <summary>
/// Fenêtre transparente posée exactement sur la zone d'image de la minimap. Elle laisse passer tous les clics
/// vers le jeu, ne prend jamais le focus et dessine le chemin autour du pion du personnage.
/// </summary>
public sealed class PathOverlayWindow : Window
{
    private readonly OverlaySurface _surface = new();

    public PathOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Content = _surface;
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    /// <summary>Place la fenêtre sur un rectangle en pixels physiques de l'écran.</summary>
    public void PlaceOver(Int32Rect screenPixels)
    {
        if (!IsVisible) Show();
        // WPF positionne et dimensionne en unités logiques : avec une mise à l'échelle d'écran (150 %, 200 %…),
        // il faut convertir les pixels physiques, sinon WPF réimpose sa propre taille.
        var dpi = VisualTreeHelper.GetDpi(this);
        double kx = dpi.DpiScaleX, ky = dpi.DpiScaleY;
        Left = screenPixels.X / kx;
        Top = screenPixels.Y / ky;
        Width = screenPixels.Width / kx;
        Height = screenPixels.Height / ky;
        _surface.PixelScale = 1 / kx;
    }

    public void Render(PathOverlayState state)
    {
        _surface.State = state;
        _surface.InvalidateVisual();
    }

    private void MakeClickThrough()
    {
        const int GWL_EXSTYLE = -20;
        const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    /// <summary>Surface de dessin : le chemin, la prochaine carte encadrée, une flèche si elle est hors de vue.</summary>
    private sealed class OverlaySurface : FrameworkElement
    {
        // Palette relevée sur l'interface de Dofus 3 (accent jaune citron, panneaux bleu nuit).
        private static readonly Brush Lime = Frozen(Color.FromRgb(0xF0, 0xFF, 0x4F));
        private static readonly Brush Orange = Frozen(Color.FromRgb(0xFF, 0x9A, 0x4D));
        private static readonly Brush Panel = Frozen(Color.FromArgb(0xE6, 0x1B, 0x1D, 0x32));
        private static readonly Pen Upcoming = new(Frozen(Color.FromArgb(0xD0, 0xF0, 0xFF, 0x4F)), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        private static readonly Pen Done = new(Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 2) { DashStyle = DashStyles.Dot };
        private static readonly Pen Halo = new(Frozen(Color.FromArgb(0x90, 0x10, 0x12, 0x20)), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        private static readonly Typeface Font = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public PathOverlayState? State { get; set; }
        public double PixelScale { get; set; } = 1;

        protected override void OnRender(DrawingContext dc)
        {
            if (State is not { } s || s.Steps.Count == 0) return;
            var k = PixelScale;
            var size = new Size(ActualWidth, ActualHeight);
            var origin = new Point(s.Marker.X * k, s.Marker.Y * k);
            double cw = s.CellWidth * k, ch = s.CellHeight * k;
            Point At(MapPosition p) => new(origin.X + (p.X - s.Current.X) * cw, origin.Y + (p.Y - s.Current.Y) * ch);

            dc.PushClip(new RectangleGeometry(new Rect(size)));
            if (s.Calibrated)
            {
                // Chemin déjà parcouru (pointillés) puis à venir (citron), à partir de l'étape en cours.
                DrawPolyline(dc, s.Steps, 0, s.Index, At, Done, null);
                DrawPolyline(dc, s.Steps, s.Index, s.Steps.Count - 1, At, Upcoming, Halo);

                if (s.Target is { } target)
                {
                    var t = At(target);
                    var box = new Rect(t.X - cw / 2 + 2, t.Y - ch / 2 + 2, cw - 4, ch - 4);
                    var accent = s.Status == PathStatus.OffPath ? Orange : Lime;
                    dc.DrawRoundedRectangle(null, new Pen(Halo.Brush, 6), box, 4, 4);
                    dc.DrawRoundedRectangle(null, new Pen(accent, 3), box, 4, 4);
                    if (!new Rect(size).Contains(t)) DrawEdgeArrow(dc, origin, t, size, accent);
                }
            }
            else if (s.Target is { } target)
            {
                // Échelle pas encore mesurée : seulement la direction de la prochaine carte.
                var dir = new Vector(target.X - s.Current.X, target.Y - s.Current.Y);
                if (dir.Length > 0) DrawEdgeArrow(dc, origin, origin + dir * 1000, size, Lime);
            }
            dc.Pop();

            DrawLabel(dc, s, size);
        }

        private static void DrawPolyline(DrawingContext dc, IReadOnlyList<MapPosition> steps, int from, int to, Func<MapPosition, Point> at, Pen pen, Pen? halo)
        {
            if (to <= from) return;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(at(steps[from]), false, false);
                for (var i = from + 1; i <= to; i++) ctx.LineTo(at(steps[i]), true, true);
            }
            geo.Freeze();
            if (halo is not null) dc.DrawGeometry(null, halo, geo);
            dc.DrawGeometry(null, pen, geo);
        }

        /// <summary>Flèche collée au bord, dans la direction de la prochaine carte quand elle est hors de vue.</summary>
        private static void DrawEdgeArrow(DrawingContext dc, Point from, Point to, Size size, Brush accent)
        {
            var dir = to - from;
            if (dir.Length < 1) return;
            dir.Normalize();
            // Point d'entrée sur le bord, avec une marge.
            var m = 16.0;
            var tx = dir.X > 0 ? (size.Width - m - from.X) / dir.X : dir.X < 0 ? (m - from.X) / dir.X : double.MaxValue;
            var ty = dir.Y > 0 ? (size.Height - m - from.Y) / dir.Y : dir.Y < 0 ? (m - from.Y) / dir.Y : double.MaxValue;
            var tip = from + dir * Math.Max(0, Math.Min(tx, ty));
            var side = new Vector(-dir.Y, dir.X);
            var head = new StreamGeometry();
            using (var ctx = head.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(tip - dir * 16 + side * 9, true, true);
                ctx.LineTo(tip - dir * 16 - side * 9, true, true);
            }
            head.Freeze();
            dc.DrawGeometry(accent, new Pen(Halo.Brush, 2), head);
        }

        private void DrawLabel(DrawingContext dc, PathOverlayState s, Size size)
        {
            var text = s.Status switch
            {
                PathStatus.Finished => $"Chemin terminé · {s.Steps.Count}/{s.Steps.Count}",
                PathStatus.OffPath => $"Hors chemin · rejoindre {s.Target}",
                _ => $"Étape {s.Index + 1}/{s.Steps.Count} · suivante {s.Target}",
            };
            if (!s.Calibrated) text += " · échelle : change de carte";
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 12, Brushes.White, dpi);
            var box = new Rect(6, 6, ft.Width + 16, ft.Height + 8);
            dc.DrawRoundedRectangle(Panel, new Pen(s.Status == PathStatus.OffPath ? Orange : Lime, 1), box, 6, 6);
            dc.DrawText(ft, new Point(box.X + 8, box.Y + 4));
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
