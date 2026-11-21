using System.Windows;
using System.Windows.Controls;

namespace DofusMulticompte.App.Controls;

/// <summary>
/// Ligne de réglage : un titre, une courte explication, et le contrôle (interrupteur, champ…) aligné à droite.
/// L'apparence est définie par le style implicite de <c>App.xaml</c>.
/// </summary>
public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
