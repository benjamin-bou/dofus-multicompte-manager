using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DofusMulticompte.Core;

namespace DofusMulticompte.App.Controls;

/// <summary>
/// Notification affichée en bas à droite de l'écran, dans le style de l'interface de Dofus 3.
/// Elle ne prend jamais le focus (le jeu garde la main) et se ferme seule ou d'un clic.
/// </summary>
public partial class ToastWindow : Window
{
    private static readonly Brush Lime = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xFF, 0x4F)));
    private static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0x4D)));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C)));

    // Pictogrammes dessinés (aucune image du jeu n'est utilisée).
    private static readonly Geometry CheckGlyph = Geometry.Parse("M 2 9 L 7 14 L 16 3");
    private static readonly Geometry ProgressGlyph = Geometry.Parse("M 2 14 L 6 9 L 10 12 L 16 4");
    private static readonly Geometry RetryGlyph = Geometry.Parse("M 15 9 A 6 6 0 1 1 12.5 4.2 M 12.5 0.5 L 12.5 4.2 L 8.8 4.2");
    private static readonly Geometry ErrorGlyph = Geometry.Parse("M 4 4 L 14 14 M 14 4 L 4 14");

    private readonly System.Windows.Threading.DispatcherTimer _timer = new();
    private bool _closing;

    public event Action<ToastWindow>? Dismissed;

    public string Key { get; }

    public ToastWindow(string key)
    {
        Key = key;
        InitializeComponent();
        Opacity = 0;
        _timer.Tick += (_, _) => Dismiss();
        MouseLeftButtonUp += (_, _) => Dismiss();
        SourceInitialized += (_, _) => MakeNonActivating();
    }

    /// <summary>Affiche (ou met à jour) le contenu et relance le délai de fermeture.</summary>
    public void Present(NotificationContent content, TimeSpan duration)
    {
        var accent = content.Kind switch
        {
            NotificationKind.Warning => Orange,
            NotificationKind.Error => Red,
            _ => Lime,
        };
        Badge.BorderBrush = accent;
        Glyph.Stroke = accent;
        Glyph.Data = content.Kind switch
        {
            NotificationKind.Progress => ProgressGlyph,
            NotificationKind.Warning => RetryGlyph,
            NotificationKind.Error => ErrorGlyph,
            _ => CheckGlyph,
        };
        Bar.Background = accent;

        TitleText.Text = content.Title.ToUpperInvariant();
        MessageText.Inlines.Clear();
        foreach (var part in content.Message)
            MessageText.Inlines.Add(new Run(part.Text) { Foreground = part.Highlight ? accent : Brushes.White });

        _timer.Stop();
        _timer.Interval = duration;
        _timer.Start();
        AnimateBar(content.Progress, duration);
    }

    /// <summary>Barre : progression x/N si fournie, sinon décompte jusqu'à la fermeture.</summary>
    private void AnimateBar(double? progress, TimeSpan duration)
    {
        var full = ((FrameworkElement)Bar.Parent).ActualWidth;
        if (full <= 0) full = Width - 24 - 28; // avant la première mise en page
        if (progress is { } p)
        {
            Bar.BeginAnimation(WidthProperty, new DoubleAnimation(full * Math.Clamp(p, 0, 1), TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            Bar.BeginAnimation(WidthProperty, new DoubleAnimation(full, 0, duration));
        }
    }

    public void SlideIn()
    {
        var targetLeft = Left;
        Left = targetLeft + 40;
        BeginAnimation(LeftProperty, new DoubleAnimation(targetLeft, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
    }

    /// <summary>Déplace la notification (empilement) en douceur.</summary>
    public void MoveTo(double top)
    {
        BeginAnimation(TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) =>
        {
            Dismissed?.Invoke(this);
            Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW : jamais de vol de focus, absent d'Alt+Tab.</summary>
    private void MakeNonActivating()
    {
        const int GWL_EXSTYLE = -20;
        const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}
