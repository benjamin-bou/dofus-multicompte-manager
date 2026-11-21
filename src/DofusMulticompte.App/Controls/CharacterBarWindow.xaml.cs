using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DofusMulticompte.App.ViewModels;

namespace DofusMulticompte.App.Controls;

/// <summary>
/// Barre flottante, toujours au premier plan : une icône par personnage en ligne, dans l'ordre de passage.
/// Elle ne prend jamais le focus (le clic part vers le jeu sans passer par l'application) et se déplace
/// par sa poignée ; sa position est mémorisée.
/// </summary>
public partial class CharacterBarWindow : Window
{
    private readonly MainViewModel _vm;
    private Point? _dragOffset;

    public CharacterBarWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        Items.ItemsSource = new ListCollectionView(vm.Characters) { Filter = o => o is CharacterViewModel { IsOnline: true } };

        Grip.MouseLeftButtonDown += OnGripDown;
        Grip.MouseMove += OnGripMove;
        Grip.MouseLeftButtonUp += OnGripUp;
        SourceInitialized += (_, _) => MakeNonActivating();

        vm.PropertyChanged += OnSettingsChanged;
        vm.Characters.CollectionChanged += OnCharactersChanged;

        ApplyOrientation();
        PlaceInitially();
        UpdateVisibility();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CharacterBarVertical)) ApplyOrientation();
        else if (e.PropertyName == nameof(MainViewModel.ShowCharacterBar)) UpdateVisibility();
    }

    private void OnCharactersChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        var show = _vm.ShowCharacterBar && _vm.Characters.Any(c => c.IsOnline);
        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
    }

    private void ApplyOrientation()
    {
        var vertical = _vm.CharacterBarVertical;
        Layout.Orientation = vertical ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;
        Items.ItemsPanel = (System.Windows.Controls.ItemsPanelTemplate)Resources[vertical ? "VerticalPanel" : "HorizontalPanel"];
        // Points de la poignée : 2 colonnes × 3 lignes à l'horizontale, l'inverse à la verticale.
        GripDots.LayoutTransform = vertical ? new RotateTransform(90) : Transform.Identity;
    }

    private void PlaceInitially()
    {
        var area = SystemParameters.WorkArea;
        var s = _vm.Settings.CharacterBar;
        if (s.Left is { } left && s.Top is { } top &&
            left >= SystemParameters.VirtualScreenLeft - 40 && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
            top >= SystemParameters.VirtualScreenTop - 10 && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
        {
            Left = left;
            Top = top;
        }
        else
        {
            // Par défaut : en haut, au centre de l'écran principal.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = area.Left + area.Width / 2 - 120;
            Top = area.Top + 4;
        }
    }

    // --- Déplacement par la poignée (DragMove n'est pas fiable sur une fenêtre qui ne s'active pas) ---

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        _dragOffset = e.GetPosition(this);
        Grip.CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMove(object sender, MouseEventArgs e)
    {
        if (_dragOffset is not { } offset || !Grip.IsMouseCaptured) return;
        var screen = PointToScreen(e.GetPosition(this));
        // PointToScreen renvoie des pixels physiques : on revient en unités WPF.
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = screen.X / dpi.DpiScaleX - offset.X;
        Top = screen.Y / dpi.DpiScaleY - offset.Y;
    }

    private void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragOffset is null) return;
        _dragOffset = null;
        Grip.ReleaseMouseCapture();
        _vm.SaveCharacterBarPosition(Left, Top);
    }

    /// <summary>WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW : ne vole jamais le focus au jeu, absente d'Alt+Tab.</summary>
    private void MakeNonActivating()
    {
        const int GWL_EXSTYLE = -20;
        const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}
