using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DofusMulticompte.App.Services;
using DofusMulticompte.App.ViewModels;

namespace DofusMulticompte.App.Controls;

/// <summary>
/// Choix de l'image d'un personnage : l'utilisateur encadre son personnage (tête, chapeau, couleurs)
/// dans une capture de sa fenêtre. L'image découpée remplace la tête de classe partout.
/// </summary>
public partial class AvatarPickerWindow : Window
{
    /// <summary>Taille de l'image enregistrée (carrée).</summary>
    private const int AvatarSize = 128;
    /// <summary>Côté minimal de la sélection, en pixels de la capture.</summary>
    private const double MinSelection = 12;

    private readonly MainViewModel _owner;
    private readonly CharacterViewModel _character;
    private readonly string _name;
    private BitmapSource? _shot;
    private Point? _anchor;
    private Int32Rect? _selection;

    private AvatarPickerWindow(MainViewModel owner, CharacterViewModel character)
    {
        _owner = owner;
        _character = character;
        _name = character.CharacterName!;
        InitializeComponent();
        Title = $"Image de {_name}";
        Heading.Text = $"Image de {_name}";

        Surface.MouseLeftButtonDown += OnDown;
        Surface.MouseMove += OnMove;
        Surface.MouseLeftButtonUp += OnUp;
        Stage.SizeChanged += (_, _) => UpdateStroke();
        UseButton.Click += (_, _) => Apply();
        RecaptureButton.Click += (_, _) => Capture();
        ResetButton.Click += (_, _) =>
        {
            _owner.SetCustomAvatar(_name, null);
            Close();
        };
        CancelButton.Click += (_, _) => Close();
        ResetButton.IsEnabled = character.HasCustomAvatar;

        Capture();
    }

    /// <summary>Ouvre la fenêtre de choix pour ce personnage, devant la fenêtre principale si elle est visible.</summary>
    public static void Open(MainViewModel owner, CharacterViewModel character)
    {
        if (character.CharacterName is null) return;
        var window = new AvatarPickerWindow(owner, character);
        if (Application.Current.MainWindow is { IsVisible: true } main && main.WindowState != WindowState.Minimized)
            window.Owner = main;
        window.Show();
        window.Activate();
    }

    private void Capture()
    {
        // Fenêtre ouverte : capture en pleine résolution. Personnage hors ligne : dernière image connue (plus petite).
        _shot = _character.IsOnline ? WindowCapture.Capture(_character.Handle, int.MaxValue) : _character.Thumbnail;
        _selection = null;
        _anchor = null;
        Selection.Visibility = Visibility.Collapsed;
        UseButton.IsEnabled = false;
        PreviewBig.ImageSource = PreviewSmall.ImageSource = null;

        NoCapture.Visibility = _shot is null ? Visibility.Visible : Visibility.Collapsed;
        Stage.Visibility = _shot is null ? Visibility.Collapsed : Visibility.Visible;
        RecaptureButton.IsEnabled = _character.IsOnline;
        if (_shot is null) return;

        Shot.Source = _shot;
        Shot.Width = Surface.Width = _shot.PixelWidth;
        Shot.Height = Surface.Height = _shot.PixelHeight;
        UpdateDim();
        UpdateStroke();
    }

    // --- Sélection carrée -------------------------------------------------------------

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_shot is null) return;
        _anchor = e.GetPosition(Surface);
        Surface.CaptureMouse();
        UpdateSelection(_anchor.Value);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_anchor is null || !Surface.IsMouseCaptured) return;
        UpdateSelection(e.GetPosition(Surface));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_anchor is null) return;
        UpdateSelection(e.GetPosition(Surface));
        _anchor = null;
        Surface.ReleaseMouseCapture();
    }

    private void UpdateSelection(Point current)
    {
        if (_shot is null || _anchor is not { } a) return;
        double w = _shot.PixelWidth, h = _shot.PixelHeight;

        // Carré dans la direction du glissement, limité aux bords de la capture.
        var dx = current.X - a.X;
        var dy = current.Y - a.Y;
        var maxX = dx >= 0 ? w - a.X : a.X;
        var maxY = dy >= 0 ? h - a.Y : a.Y;
        var side = Math.Min(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Min(maxX, maxY));
        var x = dx >= 0 ? a.X : a.X - side;
        var y = dy >= 0 ? a.Y : a.Y - side;

        var rect = new Int32Rect((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(side), (int)Math.Round(side));
        rect.Width = Math.Min(rect.Width, _shot.PixelWidth - rect.X);
        rect.Height = Math.Min(rect.Height, _shot.PixelHeight - rect.Y);
        rect.Width = rect.Height = Math.Min(rect.Width, rect.Height);

        Selection.Visibility = Visibility.Visible;
        Selection.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        Selection.Width = rect.Width;
        Selection.Height = rect.Height;

        _selection = rect.Width >= MinSelection ? rect : null;
        UseButton.IsEnabled = _selection is not null;
        UpdateDim();

        var crop = Crop();
        PreviewBig.ImageSource = PreviewSmall.ImageSource = crop;
    }

    /// <summary>Assombrit tout sauf la sélection.</summary>
    private void UpdateDim()
    {
        if (_shot is null) return;
        var full = new RectangleGeometry(new Rect(0, 0, _shot.PixelWidth, _shot.PixelHeight));
        Dim.Data = _selection is { } s
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(new Rect(s.X, s.Y, s.Width, s.Height)))
            : null;
    }

    /// <summary>Le Viewbox réduit tout, y compris le trait : on compense pour garder un trait de 2 px à l'écran.</summary>
    private void UpdateStroke()
    {
        if (_shot is null || Stage.ActualWidth <= 0) return;
        var scale = Math.Min((Stage.ActualWidth) / _shot.PixelWidth, (Stage.ActualHeight) / _shot.PixelHeight);
        if (scale > 0) Selection.StrokeThickness = 2 / scale;
    }

    private BitmapSource? Crop()
    {
        if (_shot is null || _selection is not { } s) return null;
        var cropped = new CroppedBitmap(_shot, s);
        var scale = AvatarSize / (double)s.Width;
        var resized = new TransformedBitmap(cropped, new ScaleTransform(scale, scale));
        // Copie figée : indépendante de la capture, utilisable sur tous les threads et enregistrable en PNG.
        var final = new WriteableBitmap(resized);
        final.Freeze();
        return final;
    }

    private void Apply()
    {
        if (Crop() is not { } image) return;
        _owner.SetCustomAvatar(_name, image);
        Close();
    }
}
