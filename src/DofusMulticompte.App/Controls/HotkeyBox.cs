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

        var mods = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= KeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= KeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= KeyModifiers.Win;

        SetValue(new Core.KeyGesture(mods, key.ToString()).ToString());
    }

    private void SetValue(string text)
    {
        Text = text;
        GetBindingExpression(TextProperty)?.UpdateSource();
    }
}
