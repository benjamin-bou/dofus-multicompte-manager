using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DofusMulticompte.App.Services;
using DofusMulticompte.Core.ResourcePaths;
using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.App.Controls;

/// <summary>
/// Import d'une capture de chemin : analyse automatique, aperçu du tracé reconnu, puis coordonnées de la case
/// de départ (le reste du chemin en découle). Le résultat est un <see cref="ResourcePath"/> en coordonnées du monde.
/// </summary>
public sealed class PathImportWindow : Window
{
    private readonly BitmapSource _image;
    private readonly Func<Task<MapPosition?>> _readPosition;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _name = new();
    private readonly TextBox _start = new() { Width = 110 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _save = new() { Content = "Enregistrer", Padding = new Thickness(18, 8, 18, 8), IsDefault = true };
    private readonly Button _reverse = new() { Content = "Inverser le sens", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _here = new() { Content = "Ma position actuelle", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
    private PathExtraction? _extraction;
    private List<GridCell> _cells = [];

    public ResourcePath? Result { get; private set; }

    public PathImportWindow(BitmapSource image, string suggestedName, Func<Task<MapPosition?>> readPosition)
    {
        _image = image;
        _readPosition = readPosition;
        Title = "Importer un chemin";
        Width = 1180;
        Height = 780;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(ForegroundProperty, "Text");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        _name.Text = suggestedName;
        Content = Build();
        Loaded += async (_, _) => await AnalyzeAsync();
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

        var previewCard = new Border { Child = _preview, Padding = new Thickness(8) };
        previewCard.SetResourceReference(StyleProperty, "Card");
        root.Children.Add(previewCard);

        var side = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        Grid.SetColumn(side, 1);
        root.Children.Add(side);

        side.Children.Add(Styled(new TextBlock { Text = "Importer un chemin" }, "PageTitle"));
        side.Children.Add(Styled(new TextBlock { Text = "Analyse du tracé", Margin = new Thickness(0, 18, 0, 6) }, "GroupTitle"));
        side.Children.Add(_summary);

        side.Children.Add(Styled(new TextBlock { Text = "Nom", Margin = new Thickness(0, 18, 0, 6) }, "GroupTitle"));
        side.Children.Add(_name);

        side.Children.Add(Styled(new TextBlock { Text = "Départ du chemin", Margin = new Thickness(0, 18, 0, 6) }, "GroupTitle"));
        side.Children.Add(Styled(new TextBlock
        {
            Text = "Coordonnées de la première case (repère bleu), souvent un zaap. Place-toi dessus en jeu et clique sur « Ma position actuelle », ou saisis-les (« -1, 24 »).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        }, "Hint"));
        var startRow = new StackPanel { Orientation = Orientation.Horizontal };
        startRow.Children.Add(_start);
        startRow.Children.Add(_here);
        side.Children.Add(startRow);
        side.Children.Add(_reverse);
        side.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        _save.SetResourceReference(StyleProperty, "PrimaryButton");
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        side.Children.Add(buttons);

        _save.IsEnabled = false;
        _reverse.IsEnabled = false;
        _save.Click += (_, _) => Save();
        _reverse.Click += (_, _) => { _cells.Reverse(); DrawPreview(); };
        _here.Click += async (_, _) =>
        {
            _here.IsEnabled = false;
            var p = await _readPosition();
            _here.IsEnabled = true;
            if (p is { } pos) _start.Text = $"{pos.X},{pos.Y}";
            else ShowError("Position illisible : un personnage doit être en jeu, avec les coordonnées visibles en haut à gauche.");
        };
        return root;
    }

    private static T Styled<T>(T element, string style) where T : FrameworkElement
    {
        element.SetResourceReference(StyleProperty, style);
        return element;
    }

    private async Task AnalyzeAsync()
    {
        _summary.Text = "Analyse en cours…";
        var px = MinimapLocator.Pixels(_image, out var w, out var h);
        _extraction = await Task.Run(() => PathImageAnalyzer.Analyze(px, w, h, w * 4));
        if (_extraction is null || _extraction.Cells.Count < 2)
        {
            _summary.Text = "Aucun chemin reconnu. La capture doit montrer la carte du monde avec son quadrillage et un tracé rouge.";
            _preview.Source = _image;
            return;
        }
        _cells = _extraction.Cells.ToList();
        var e = _extraction;
        _summary.Text = $"{_cells.Count} étapes · quadrillage {e.Grid.CellWidth:F1} × {e.Grid.CellHeight:F1} px · {e.SegmentsUsed}/{e.SegmentsFound} traits suivis"
            + (e.IsComplete ? "." : ". Certains traits rouges n'ont pas pu être reliés au chemin : vérifie l'aperçu.");
        _save.IsEnabled = true;
        _reverse.IsEnabled = true;
        DrawPreview();
    }

    /// <summary>Capture + chemin reconnu (vert), départ (bleu), arrivée (rouge) et numéros d'étapes.</summary>
    private void DrawPreview()
    {
        if (_extraction is null) return;
        var g = _extraction.Grid;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(_image, new Rect(0, 0, _image.PixelWidth, _image.PixelHeight));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(230, 40, 230, 90)), Math.Max(3, g.CellWidth / 18)) { LineJoin = PenLineJoin.Round };
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(Center(_cells[0]), false, false);
                foreach (var c in _cells.Skip(1)) ctx.LineTo(Center(c), true, true);
            }
            dc.DrawGeometry(null, pen, geo);

            var font = new Typeface("Segoe UI");
            var r = Math.Max(8, g.CellHeight / 5);
            for (var i = 0; i < _cells.Count; i += 10)
            {
                var p = Center(_cells[i]);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(220, 20, 22, 36)), null, p, r * 1.3, r);
                var ft = new FormattedText((i + 1).ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, font, r * 1.1, Brushes.White, 1);
                dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
            }
            dc.DrawEllipse(Brushes.DeepSkyBlue, new Pen(Brushes.White, 2), Center(_cells[0]), r * 1.2, r * 1.2);
            dc.DrawEllipse(Brushes.OrangeRed, new Pen(Brushes.White, 2), Center(_cells[^1]), r, r);
        }
        var bmp = new RenderTargetBitmap(_image.PixelWidth, _image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        _preview.Source = bmp;

        Point Center(GridCell c)
        {
            var (x, y) = g.CenterOf(c);
            return new Point(x, y);
        }
    }

    private void Save()
    {
        if (_cells.Count < 2) return;
        if (PositionParser.Parse(_start.Text) is not { } start)
        {
            ShowError("Indique les coordonnées de la case de départ, par exemple « 5,-18 ».");
            return;
        }
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("Donne un nom au chemin.");
            return;
        }
        Result = new ResourcePath { Name = name, Steps = ResourcePath.ToWorld(_cells, _cells[0], start) };
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.SetResourceReference(ForegroundProperty, "Warn");
    }
}
