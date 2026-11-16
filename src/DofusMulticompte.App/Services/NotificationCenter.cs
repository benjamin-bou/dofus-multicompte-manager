using System.Windows;
using DofusMulticompte.App.Controls;
using DofusMulticompte.Core;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Empile les notifications en bas à droite de l'écran principal. Une notification qui porte la même
/// clé qu'une notification visible la met à jour au lieu d'en ajouter une (progression « 1/3 », « 2/3 »…).
/// À utiliser depuis le thread de l'interface.
/// </summary>
public sealed class NotificationCenter
{
    private const double ScreenMargin = 4; // la fenêtre a déjà 12 px de marge pour l'ombre
    private const double Gap = -8;         // les marges d'ombre se chevauchent
    private const int MaxVisible = 4;

    private readonly List<ToastWindow> _toasts = [];

    public void Show(string key, NotificationContent content, TimeSpan duration)
    {
        var existing = _toasts.FirstOrDefault(t => t.Key == key);
        if (existing is not null)
        {
            existing.Present(content, duration);
            return;
        }

        // Trop de notifications : la plus ancienne laisse sa place.
        if (_toasts.Count >= MaxVisible) _toasts[0].Dismiss();

        var toast = new ToastWindow(key);
        toast.Dismissed += t =>
        {
            _toasts.Remove(t);
            Layout();
        };
        _toasts.Add(toast);

        var area = SystemParameters.WorkArea;
        toast.Left = area.Right - toast.Width - ScreenMargin;
        toast.Top = area.Bottom; // hors écran le temps de mesurer la hauteur
        toast.Show();
        toast.Present(content, duration);
        toast.UpdateLayout();
        Layout(newest: toast);
        toast.SlideIn();
    }

    /// <summary>La plus récente en bas, les autres au-dessus. La nouvelle se pose directement, les autres glissent.</summary>
    private void Layout(ToastWindow? newest = null)
    {
        var bottom = SystemParameters.WorkArea.Bottom - ScreenMargin;
        for (var i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];
            var top = bottom - t.ActualHeight;
            if (t == newest) t.Top = top;
            else t.MoveTo(top);
            bottom = top - Gap;
        }
    }
}
