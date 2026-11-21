using System.Windows.Input;
using DofusMulticompte.App.Services;
using DofusMulticompte.Core;
using TextBox = System.Windows.Controls.TextBox;

namespace DofusMulticompte.App.Controls;

/// <summary>Champ qui enregistre la combinaison de touches pressée. Retour arrière ou Suppr pour effacer.</summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        // Un contrôle dérivé ne reçoit pas le style implicite de TextBox : on le rattache explicitement.
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        ToolTip = "Clique puis appuie sur la touche ou la combinaison voulue. Retour arrière pour effacer.";
    }

    private bool _allowMouse;

    /// <summary>Accepte aussi les boutons latéraux de la souris et le clic molette (raccourcis globaux uniquement).</summary>
    public bool AllowMouse
    {
        get => _allowMouse;
        set
        {
            _allowMouse = value;
            ToolTip = value
                ? "Clique puis appuie sur la touche, la combinaison, ou un bouton de souris (latéral ou molette). Retour arrière pour effacer."
                : "Clique puis appuie sur la touche ou la combinaison voulue. Retour arrière pour effacer.";
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        // Laisse passer les boutons de souris déjà associés à un raccourci, pour pouvoir les réattribuer.
        if (AllowMouse) MouseHotkeyHook.Suspended = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (AllowMouse) MouseHotkeyHook.Suspended = false;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (AllowMouse && IsKeyboardFocused)
        {
            var button = e.ChangedButton switch
            {
                System.Windows.Input.MouseButton.XButton1 => Core.MouseButton.X1,
                System.Windows.Input.MouseButton.XButton2 => Core.MouseButton.X2,
                System.Windows.Input.MouseButton.Middle => Core.MouseButton.Middle,
                _ => Core.MouseButton.None,
            };
            if (button != Core.MouseButton.None)
            {
                e.Handled = true;
                SetValue(new Core.KeyGesture(CurrentModifiers(), Core.KeyGesture.MouseButtonName(button)).ToString());
                return;
            }
        }
        base.OnPreviewMouseDown(e);
    }

    private static KeyModifiers CurrentModifiers()
    {
        var mods = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= KeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= KeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= KeyModifiers.Win;
        return mods;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = false; // laisse la navigation au clavier fonctionner
            return;
        }
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            SetValue("");
            return;
        }
        if (KeyMap.IsModifier(key) || key == Key.None) return;

        SetValue(new Core.KeyGesture(CurrentModifiers(), key.ToString()).ToString());
    }

    private void SetValue(string text)
    {
        Text = text;
        GetBindingExpression(TextProperty)?.UpdateSource();
    }
}
